using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using XboxAuthNet.OAuth;
using XboxAuthNet.OAuth.CodeFlow;
using XboxAuthNet.XboxLive;
using XboxAuthNet.XboxLive.Requests;
using XboxAuthNet.XboxLive.Responses;

namespace XAU.Services;

public class OAuthService : IOAuthService
{
    private static readonly byte[] AuthFileMagic = Encoding.ASCII.GetBytes("XAU1");
    private static readonly byte[] AuthDpapiEntropy = Encoding.UTF8.GetBytes("XAU-Auth-v1");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthService> _logger;

    private readonly string _authFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "auth.json");

    private CodeFlowAuthenticator? _oauth;
    private XboxAuthClient? _xboxAuthClient;
    private XboxSignedClient? _xboxSignedClient;

    public OAuthService(IHttpClientFactory httpClientFactory, ILogger<OAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private void EnsureInitialized(object? uiParent)
    {
        if (_oauth != null)
            return;

        var httpClient = _httpClientFactory.CreateClient("OAuth");
        var apiClient = new CodeFlowLiveApiClient(XboxGameTitles.XboxAppPC, XboxAuthConstants.XboxScope, httpClient);
        _xboxAuthClient = new XboxAuthClient(httpClient);
        _xboxSignedClient = new XboxSignedClient(httpClient);

        var builder = new CodeFlowBuilder(apiClient);
        if (uiParent != null)
        {
            builder.WithUIParent(uiParent);
        }

        _oauth = builder.Build();
    }

    public async Task<AuthTokensResult?> TryRestoreSessionAsync()
    {
        if (!File.Exists(_authFilePath))
            return null;

        try
        {
            EnsureInitialized(null);
            var savedResponse = ReadSession();
            if (savedResponse == null || !savedResponse.Validate() || string.IsNullOrEmpty(savedResponse.RefreshToken))
            {
                DeleteAuthFile();
                return null;
            }

            var refreshedResponse = await _oauth!.AuthenticateSilently(savedResponse.RefreshToken);
            WriteSession(refreshedResponse);
            return await GenerateTokensAsync(refreshedResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to silently restore saved OAuth session");
            DeleteAuthFile();
            return null;
        }
    }

    public async Task<AuthTokensResult?> AuthenticateInteractivelyAsync(object? uiParent = null)
    {
        try
        {
            EnsureInitialized(uiParent);
            var response = await _oauth!.AuthenticateInteractively();
            WriteSession(response);
            return await GenerateTokensAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interactive OAuth authentication failed");
            return null;
        }
    }

    public void Signout()
    {
        try
        {
            EnsureInitialized(null);
            _oauth?.Signout();
        }
        catch { }
        finally
        {
            DeleteAuthFile();
        }
    }

    private async Task<AuthTokensResult?> GenerateTokensAsync(MicrosoftOAuthResponse response)
    {
        try
        {
            var deviceTokenResponse = await _xboxSignedClient!.RequestDeviceToken(XboxDeviceTypes.Win32, "0.0.0");
            var deviceToken = deviceTokenResponse.Token;
            var sisuResult = await _xboxSignedClient.SisuAuth(new XboxSisuAuthRequest
            {
                AccessToken = response.AccessToken,
                ClientId = XboxGameTitles.XboxAppPC,
                DeviceToken = deviceToken,
                RelyingParty = XboxAuthConstants.XboxLiveRelyingParty,
            });

            var xui = sisuResult.AuthorizationToken?.XuiClaims;
            var userHash = xui?.UserHash ?? "";
            var token = sisuResult.AuthorizationToken?.Token ?? "";
            var xauthToken = $"XBL3.0 x={userHash};{token}";
            var spoofToken = await BuildSpoofXauthAsync(sisuResult, deviceToken) ?? xauthToken;
            var xuid = xui?.XboxUserId ?? "";
            var gamertag = xui?.Gamertag ?? "Unknown";

            return new AuthTokensResult(xauthToken, spoofToken, xuid, gamertag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Sisu/XAUTH tokens from OAuth response");
            return null;
        }
    }

    private async Task<string?> BuildSpoofXauthAsync(XboxSisuResponse sisuResult, string deviceToken)
    {
        if (sisuResult.UserToken?.Token == null)
            return null;

        var relyingParties = new[]
        {
            XboxAuthConstants.XboxUserPresenceRelyingParty,
            "https://userpresence.xboxlive.com",
            XboxAuthConstants.XboxLiveRelyingParty,
        };

        foreach (var relyingParty in relyingParties)
        {
            try
            {
                var signedXsts = await _xboxSignedClient!.RequestSignedXstsToken(new XboxSignedXstsRequest
                {
                    UserToken = sisuResult.UserToken.Token,
                    DeviceToken = deviceToken,
                    TitleToken = sisuResult.TitleToken?.Token,
                    RelyingParty = relyingParty,
                });

                if (signedXsts?.Token != null && signedXsts.XuiClaims?.UserHash != null)
                {
                    return $"XBL3.0 x={signedXsts.XuiClaims.UserHash};{signedXsts.Token}";
                }
            }
            catch { }

            try
            {
                var xsts = await _xboxAuthClient!.RequestXsts(new XboxXstsRequest
                {
                    UserToken = sisuResult.UserToken.Token,
                    DeviceToken = deviceToken,
                    TitleToken = sisuResult.TitleToken?.Token,
                    RelyingParty = relyingParty,
                });

                if (xsts?.Token != null && xsts.XuiClaims?.UserHash != null)
                {
                    return $"XBL3.0 x={xsts.XuiClaims.UserHash};{xsts.Token}";
                }
            }
            catch { }
        }

        return null;
    }

    private MicrosoftOAuthResponse? ReadSession()
    {
        var raw = File.ReadAllBytes(_authFilePath);
        string json;
        if (raw.Length >= AuthFileMagic.Length && raw.AsSpan(0, AuthFileMagic.Length).SequenceEqual(AuthFileMagic))
        {
            var encrypted = raw.AsSpan(AuthFileMagic.Length).ToArray();
            var plain = ProtectedData.Unprotect(encrypted, AuthDpapiEntropy, DataProtectionScope.CurrentUser);
            json = Encoding.UTF8.GetString(plain);
        }
        else
        {
            json = Encoding.UTF8.GetString(raw);
        }
        return JsonConvert.DeserializeObject<MicrosoftOAuthResponse>(json);
    }

    private void WriteSession(MicrosoftOAuthResponse response)
    {
        var json = JsonConvert.SerializeObject(response);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, AuthDpapiEntropy, DataProtectionScope.CurrentUser);
        var dir = Path.GetDirectoryName(_authFilePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(_authFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(AuthFileMagic, 0, AuthFileMagic.Length);
        fs.Write(encrypted, 0, encrypted.Length);
    }

    private void DeleteAuthFile()
    {
        try { File.Delete(_authFilePath); } catch { }
    }
}
