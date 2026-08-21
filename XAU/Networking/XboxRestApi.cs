using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XAU.Services;
using XAU.ViewModels.Pages;
using XAU.ViewModels.Windows;

public class XboxRestAPI
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _eventBasedClient;
    private readonly HttpClient _spooferClient;

    private readonly ISessionService? _sessionService;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<XboxRestAPI> _logger;
    private readonly string? _explicitXauth;

    public XboxRestAPI(
        IHttpClientFactory httpClientFactory,
        ISessionService sessionService,
        ISettingsService settingsService,
        ILogger<XboxRestAPI> logger)
    {
        _httpClient = httpClientFactory.CreateClient("XboxRestAPI");
        _spooferClient = httpClientFactory.CreateClient("XboxSpoofer");
        _eventBasedClient = httpClientFactory.CreateClient("XboxEvents");
        _sessionService = sessionService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public XboxRestAPI(string xauth)
    {
        _explicitXauth = SanitizeXauth(xauth);
        _logger = NullLogger<XboxRestAPI>.Instance;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
        _spooferClient = new HttpClient(handler);

        var insecureEventsHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _eventBasedClient = new HttpClient(insecureEventsHandler);
    }

    private string CurrentXauth =>
        !string.IsNullOrEmpty(_explicitXauth)
            ? _explicitXauth
            : (_sessionService?.XAuthToken ?? "");

    private string CurrentSpoofAuth =>
        !string.IsNullOrEmpty(_explicitXauth)
            ? _explicitXauth
            : (_sessionService?.EffectiveSpoofToken ?? "");

    private string CurrentResponseLanguage =>
        (_settingsService?.Current.RegionOverride ?? false)
            ? "en-GB"
            : System.Globalization.CultureInfo.CurrentCulture.Name;

    public static string GetSpoofAuth() =>
        HomeViewModel.SpoofXAUTH;

    public static string SanitizeXauthPublic(string xauth) => SanitizeXauth(xauth);

    private static string SanitizeXauth(string xauth)
    {
        if (string.IsNullOrWhiteSpace(xauth))
        {
            return "";
        }

        var trimmed = xauth.Trim();
        var startIndex = trimmed.IndexOf("XBL3.0 x=", StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return trimmed;
        }

        var semicolonIndex = trimmed.IndexOf(';', startIndex);
        if (semicolonIndex < 0)
        {
            return trimmed.Substring(startIndex).Trim();
        }

        var endIndex = semicolonIndex + 1;
        while (endIndex < trimmed.Length)
        {
            var c = trimmed[endIndex];
            if (char.IsWhiteSpace(c) || c == '\0')
            {
                break;
            }

            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '=')
            {
                endIndex++;
                continue;
            }

            break;
        }

        return trimmed.Substring(startIndex, endIndex - startIndex);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? contractVersion = null, string? host = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(HeaderNames.Authorization, CurrentXauth);
        request.Headers.Add(HeaderNames.AcceptLanguage, CurrentResponseLanguage);
        request.Headers.Add(HeaderNames.Accept, HeaderValues.Accept);

        if (!string.IsNullOrEmpty(contractVersion))
        {
            request.Headers.Add(HeaderNames.ContractVersion, contractVersion);
        }

        if (!string.IsNullOrEmpty(host))
        {
            request.Headers.Add(HeaderNames.Host, host);
        }

        request.Headers.Add(HeaderNames.Connection, HeaderValues.KeepAlive);
        return request;
    }

    public async Task<BasicProfile?> GetBasicProfileAsync()
    {
        using var request = CreateRequest(HttpMethod.Get, BasicXboxAPIUris.GamertagUrl, HeaderValues.ContractVersion2, Hosts.Profile);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<BasicProfile>(content);
    }

    public async Task<Profile?> GetProfileAsync(string xuid)
    {
        var url = string.Format(InterpolatedXboxAPIUrls.ProfileUrl, xuid);
        using var request = CreateRequest(HttpMethod.Get, url, HeaderValues.ContractVersion5, Hosts.PeopleHub);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<Profile>(content);
    }

    public async Task<GameTitle?> GetGameTitleAsync(string xuid, string titleId)
    {
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.TitleUrl, xuid);
        using var request = CreateRequest(HttpMethod.Post, url, HeaderValues.ContractVersion2);

        var gameTitleRequest = new GameTitleRequest
        {
            Pfns = null,
            TitleIds = new List<string> { titleId }
        };

        request.Content = new StringContent(
            JsonConvert.SerializeObject(gameTitleRequest), Encoding.UTF8, HeaderValues.Accept);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<GameTitle>(content);
    }

    public async Task<Gamepass?> GetGamepassMembershipAsync(string xuid)
    {
        if (string.IsNullOrWhiteSpace(xuid))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.GamepassMembershipUrl, xuid);
        using var request = CreateRequest(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<Gamepass>(content);
    }

    public async Task<TitlesList?> GetGamesListAsync(string xuid)
    {
        if (string.IsNullOrWhiteSpace(xuid))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.TitlesUrl, xuid);
        using var request = CreateRequest(HttpMethod.Get, url, HeaderValues.ContractVersion2, Hosts.TitleHub);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<TitlesList>(content);
    }

    public async Task<JObject?> GetGamertagProfileAsync(string gamertag)
    {
        if (string.IsNullOrWhiteSpace(gamertag))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.GamertagSearch, Uri.EscapeDataString(gamertag));
        using var request = CreateRequest(HttpMethod.Get, url, HeaderValues.ContractVersion2, Hosts.Profile);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JObject.Parse(content);
    }

    public async Task<GameStatsResponse?> GetGameStatsAsync(string xuid, string titleId)
    {
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        using var request = CreateRequest(HttpMethod.Post, BasicXboxAPIUris.UserStatsUrl, HeaderValues.ContractVersion2);
        var stat = new GameStat { TitleId = titleId };
        var gameStatsRequest = new GameStatsRequest
        {
            Xuids = new List<string> { xuid },
            Stats = new List<GameStat> { stat }
        };

        request.Content = new StringContent(
            JsonConvert.SerializeObject(gameStatsRequest), Encoding.UTF8, HeaderValues.Accept);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<GameStatsResponse>(content);
    }

    private HttpRequestMessage CreatePresenceRequest(HttpMethod method, string url, string? contractVersion = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(HeaderNames.Authorization, CurrentSpoofAuth);
        request.Headers.Add(HeaderNames.AcceptLanguage, CurrentResponseLanguage);
        request.Headers.Add(HeaderNames.Accept, HeaderValues.Accept);

        if (!string.IsNullOrEmpty(contractVersion))
        {
            request.Headers.Add(HeaderNames.ContractVersion, contractVersion);
        }

        return request;
    }

    private async Task<SpoofResult> PostSpoofAsync(string url, string requestBody, string apiName, string contractVersion = HeaderValues.ContractVersion3)
    {
        using var request = CreatePresenceRequest(HttpMethod.Post, url, contractVersion);
        if (!string.IsNullOrEmpty(requestBody))
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, HeaderValues.Accept);
        }

        var response = await _spooferClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return SpoofResult.Ok();
        }

        var isRateLimited = response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.Headers.TryGetValues("Retry-After", out var retryAfterValues) && retryAfterValues.Any());

        var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;

        var message = isRateLimited
            ? "Xbox is rate limiting spoofing requests. Wait a moment before trying again."
            : isForbidden
                ? "Xbox rejected the spoof request (403 Forbidden). Refresh your token and try again."
                : $"{apiName} failed with {(int)response.StatusCode} {response.StatusCode}. {responseBody}";

        return SpoofResult.Fail(message);
    }

    public async Task<SpoofResult> SendHeartbeatAsync(string xuid, string spoofedTitleId)
    {
        spoofedTitleId = spoofedTitleId.Trim();
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(spoofedTitleId))
        {
            return SpoofResult.Fail("Missing XUID or Title ID.");
        }

        if (!ulong.TryParse(spoofedTitleId, out var titleId))
        {
            return SpoofResult.Fail("Title ID must be numeric.");
        }

        var requestBody = $"{{\"titles\":[{{\"expiration\":600,\"id\":{titleId},\"state\":\"active\",\"sandbox\":\"RETAIL\"}}]}}";
        return await PostSpoofAsync(
            string.Format(InterpolatedXboxAPIUrls.HeartbeatUrl, xuid),
            requestBody,
            "Heartbeat");
    }

    public async Task<SpoofResult> SendHeartbeatMeAsync(string spoofedTitleId)
    {
        spoofedTitleId = spoofedTitleId.Trim();
        if (string.IsNullOrWhiteSpace(spoofedTitleId))
        {
            return SpoofResult.Fail("Missing Title ID.");
        }

        if (!ulong.TryParse(spoofedTitleId, out var titleId))
        {
            return SpoofResult.Fail("Title ID must be numeric.");
        }

        var requestBody = $"{{\"titles\":[{{\"expiration\":600,\"id\":{titleId},\"state\":\"active\",\"sandbox\":\"RETAIL\"}}]}}";
        return await PostSpoofAsync(InterpolatedXboxAPIUrls.HeartbeatMeUrl, requestBody, "Heartbeat (me)");
    }

    public async Task<SpoofResult> SendPresenceAsync(string xuid, string spoofedTitleId)
    {
        spoofedTitleId = spoofedTitleId.Trim();
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(spoofedTitleId))
        {
            return SpoofResult.Fail("Missing XUID or Title ID.");
        }

        if (!ulong.TryParse(spoofedTitleId, out var titleId))
        {
            return SpoofResult.Fail("Title ID must be numeric.");
        }

        var presenceRequest = new PresenceTitleRequest
        {
            id = titleId
        };

        return await PostSpoofAsync(
            string.Format(InterpolatedXboxAPIUrls.PresenceUrl, xuid),
            JsonConvert.SerializeObject(presenceRequest),
            "Presence");
    }

    public async Task<SpoofResult> SendPresenceMeAsync(string spoofedTitleId)
    {
        spoofedTitleId = spoofedTitleId.Trim();
        if (string.IsNullOrWhiteSpace(spoofedTitleId))
        {
            return SpoofResult.Fail("Missing Title ID.");
        }

        if (!ulong.TryParse(spoofedTitleId, out var titleId))
        {
            return SpoofResult.Fail("Title ID must be numeric.");
        }

        var presenceRequest = new PresenceTitleRequest
        {
            id = titleId
        };

        return await PostSpoofAsync(
            InterpolatedXboxAPIUrls.PresenceMeUrl,
            JsonConvert.SerializeObject(presenceRequest),
            "Presence (me)");
    }

    public async Task<SpoofResult> SendSpoofAsync(string xuid, string spoofedTitleId)
    {
        if (string.IsNullOrWhiteSpace(CurrentXauth))
        {
            return SpoofResult.Fail("Missing XAUTH token. Log in again.");
        }

        var attempts = new List<SpoofResult>();

        var presence = await SendPresenceAsync(xuid, spoofedTitleId);
        if (presence.Success)
        {
            _ = await SendHeartbeatAsync(xuid, spoofedTitleId);
            return presence;
        }
        attempts.Add(presence);

        var presenceMe = await SendPresenceMeAsync(spoofedTitleId);
        if (presenceMe.Success)
        {
            _ = await SendHeartbeatAsync(xuid, spoofedTitleId);
            return presenceMe;
        }
        attempts.Add(presenceMe);

        var heartbeat = await SendHeartbeatAsync(xuid, spoofedTitleId);
        if (heartbeat.Success)
        {
            return heartbeat;
        }
        attempts.Add(heartbeat);

        var heartbeatMe = await SendHeartbeatMeAsync(spoofedTitleId);
        if (heartbeatMe.Success)
        {
            return heartbeatMe;
        }
        attempts.Add(heartbeatMe);

        return SpoofResult.Fail(string.Join(" | ", attempts.Select(a => a.Error).Where(e => !string.IsNullOrWhiteSpace(e))));
    }

    public async Task StopHeartbeatAsync(string xuid)
    {
        if (string.IsNullOrWhiteSpace(xuid))
        {
            return;
        }

        try
        {
            using var r1 = CreatePresenceRequest(HttpMethod.Delete, string.Format(InterpolatedXboxAPIUrls.HeartbeatUrl, xuid), HeaderValues.ContractVersion3);
            await _spooferClient.SendAsync(r1);

            using var r2 = CreatePresenceRequest(HttpMethod.Delete, InterpolatedXboxAPIUrls.HeartbeatMeUrl, HeaderValues.ContractVersion3);
            await _spooferClient.SendAsync(r2);

            using var r3 = CreatePresenceRequest(HttpMethod.Delete, string.Format(InterpolatedXboxAPIUrls.PresenceUrl, xuid), HeaderValues.ContractVersion3);
            await _spooferClient.SendAsync(r3);

            using var r4 = CreatePresenceRequest(HttpMethod.Delete, InterpolatedXboxAPIUrls.PresenceMeUrl, HeaderValues.ContractVersion3);
            await _spooferClient.SendAsync(r4);
        }
        catch { }
    }

    public async Task<AchievementsResponse?> GetAchievementsForTitleAsync(string xuid, string titleId)
    {
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.QueryAchievementsUrl, xuid, titleId);
        using var request = CreateRequest(HttpMethod.Get, url, HeaderValues.ContractVersion4, Hosts.Achievements);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<AchievementsResponse>(content);
    }

    public async Task<Xbox360AchievementResponse?> GetAchievementsFor360TitleAsync(string xuid, string titleId)
    {
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        var url = string.Format(InterpolatedXboxAPIUrls.QueryAchievements360Url, xuid, titleId);
        using var request = CreateRequest(HttpMethod.Get, url, HeaderValues.ContractVersion3, Hosts.Achievements);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<Xbox360AchievementResponse>(content);
    }

    public async Task UnlockTitleBasedAchievementAsync(string serviceConfigId, string titleId, string xuid, string achievementId, bool useFakeSignature = false)
    {
        await UnlockTitleBasedAchievementsAsync(serviceConfigId, titleId, xuid, new List<string> { achievementId }, useFakeSignature);
    }

    public async Task UnlockTitleBasedAchievementsAsync(string serviceConfigId, string titleId, string xuid, List<string> achievementIds, bool useFakeSignature = false)
    {
        if (string.IsNullOrWhiteSpace(serviceConfigId) || string.IsNullOrWhiteSpace(titleId) || string.IsNullOrWhiteSpace(xuid) || achievementIds.Count == 0)
        {
            return;
        }

        const int chunkSize = 50;
        for (int i = 0; i < achievementIds.Count; i += chunkSize)
        {
            var chunk = achievementIds.Skip(i).Take(chunkSize).ToList();

            var unlockRequest = new UnlockTitleBasedAchievementRequest
            {
                titleId = titleId,
                serviceConfigId = serviceConfigId,
                userId = xuid,
                achievements = chunk.Select(id => new AchievementsArrayEntry { id = id, percentComplete = "100" }).ToList()
            };

            var unlockBodyStr = JsonConvert.SerializeObject(unlockRequest);
            var url = string.Format(InterpolatedXboxAPIUrls.UpdateAchievementsUrl, xuid, serviceConfigId);

            using var request = CreateRequest(HttpMethod.Post, url, HeaderValues.ContractVersion2, Hosts.Achievements);
            request.Headers.Add("User-Agent", "XboxServicesAPI/2021.10.20211005.0 c");

            if (useFakeSignature)
            {
                request.Headers.Add(HeaderNames.Signature, HeaderValues.Signature);
            }

            request.Content = new StringContent(unlockBodyStr, Encoding.UTF8, HeaderValues.Accept);

            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException($"Failed to unlock achievement(s) for title {titleId} with status code {response.StatusCode}");
            }
        }
    }

    public async Task UnlockEventBasedAchievement(string eventsToken, StringContent requestBody)
    {
        if (string.IsNullOrWhiteSpace(eventsToken))
        {
            return;
        }

        var authxtoken = Regex.Replace(CurrentXauth, @"XBL3\.0 x=\d+;", "XBL3.0 x=-;");
        using var request = new HttpRequestMessage(HttpMethod.Post, BasicXboxAPIUris.TelemetryUrl);
        request.Headers.Add("user-agent", "MSDW");
        request.Headers.Add("cache-control", "no-cache");
        request.Headers.Add(HeaderNames.Accept, HeaderValues.Accept);
        request.Headers.Add("reliability-mode", "standard");
        request.Headers.Add("client-version", "EUTC-Windows-C++-no-10.0.22621.3296.amd64fre.ni_release.220506-1250-no");
        request.Headers.Add("apikey", "0890af88a9ed4cc886a14f5e174a2827-9de66c5e-f867-43a8-a7b8-e0ddd481cca4-7548,95c1f21d6cb047a09e7b423c1cb2222e-9965f07b-54fa-498e-9727-9e8d24dec39e-7027");
        request.Headers.Add("Client-Id", "NO_AUTH");
        request.Headers.Add(HeaderNames.Host, Hosts.Telemetry);
        request.Headers.Add(HeaderNames.Connection, "close");
        request.Headers.Add("authxtoken", authxtoken);
        request.Headers.Add("tickets", $"\"1\"=\"{eventsToken}\"");
        request.Content = requestBody;

        var response = await _eventBasedClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("POST {TelemetryUrl} => {StatusCode}", BasicXboxAPIUris.TelemetryUrl, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Event unlock failed: {Body}", responseBody);
        }
    }

    public async Task<GamePassProducts?> GetTitleIdsFromGamePass(string prodId)
    {
        if (string.IsNullOrWhiteSpace(prodId))
        {
            return null;
        }

        using var request = CreateRequest(HttpMethod.Post, BasicXboxAPIUris.GamepassCatalogUrl);
        var gamepassProducts = new GamepassProductsRequest
        {
            Products = new List<string> { prodId }
        };
        request.Content = new StringContent(
            JsonConvert.SerializeObject(gamepassProducts), Encoding.UTF8, HeaderValues.Accept);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonConvert.DeserializeObject<GamePassProducts>(content);
    }
}
