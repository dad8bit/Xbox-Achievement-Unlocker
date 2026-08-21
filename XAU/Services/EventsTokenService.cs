using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using XAU.Util.Etw;

namespace XAU.Services;

public class EventsTokenService : IEventsTokenService
{
    private readonly ISessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<EventsTokenService> _logger;

    public EventsTokenService(
        ISessionService sessionService,
        ISettingsService settingsService,
        ILogger<EventsTokenService> logger)
    {
        _sessionService = sessionService;
        _settingsService = settingsService;
        _logger = logger;
        RestoreCachedToken();
    }

    public bool IsTokenValid()
    {
        return !_sessionService.IsEventsTokenExpired();
    }

    public void RestoreCachedToken()
    {
        var settings = _settingsService.Current;
        if (!string.IsNullOrEmpty(settings.CachedEventsToken) && settings.EventsTokenObtainedAt.HasValue)
        {
            var age = DateTime.UtcNow - settings.EventsTokenObtainedAt.Value;
            if (age < TimeSpan.FromHours(23))
            {
                _sessionService.EventsToken = settings.CachedEventsToken;
                _sessionService.EventsTokenObtainedAt = settings.EventsTokenObtainedAt.Value;
                _sessionService.EventsUserHash = settings.EventsUserHash;
                _logger.LogInformation("Restored cached events token (age: {AgeHours:F1}h)", age.TotalHours);
            }
        }
    }

    public void PersistCachedToken()
    {
        var settings = _settingsService.Current;
        settings.CachedEventsToken = _sessionService.EventsToken;
        settings.EventsTokenObtainedAt = _sessionService.EventsTokenObtainedAt;
        settings.EventsUserHash = _sessionService.EventsUserHash;
        _settingsService.SaveSettings(settings);
    }

    public async Task<string?> GrabEventsTokenAsync(bool launchSolitaireIfMissing = true, Action<string>? progressCallback = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                progressCallback?.Invoke("Starting ETW trace...");
                _logger.LogInformation("Starting ETW trace...");
                EtwTokenCapture.Cleanup();
                var method = EtwTokenCapture.Start();
                if (method == null)
                {
                    _logger.LogWarning("Failed to start ETW trace (administrative privileges may be required).");
                    return null;
                }

                var alreadyRunning = Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0;
                var launchedByUs = false;

                if (!alreadyRunning && launchSolitaireIfMissing)
                {
                    try
                    {
                        progressCallback?.Invoke("Launching Solitaire...");
                        _logger.LogInformation("Launching Solitaire...");
                        var p = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                UseShellExecute = true,
                                FileName = AppLaunchUris.Solitaire
                            }
                        };
                        p.Start();
                        launchedByUs = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to launch Solitaire");
                        EtwTokenCapture.Stop(method);
                        EtwTokenCapture.CleanupFiles();
                        return null;
                    }

                    // Wait for the process to appear
                    var appeared = false;
                    for (var i = 0; i < 15; i++)
                    {
                        await Task.Delay(1000);
                        if (Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0)
                        {
                            appeared = true;
                            break;
                        }
                    }

                    if (!appeared)
                    {
                        _logger.LogWarning("Solitaire process never appeared after 15s");
                        EtwTokenCapture.Stop(method);
                        EtwTokenCapture.CleanupFiles();
                        return null;
                    }
                }

                progressCallback?.Invoke("Capturing telemetry events...");
                await Task.Delay(25000);

                EtwTokenCapture.Stop(method);
                await Task.Delay(2000);

                var token = EtwTokenCapture.ExtractTokens();
                EtwTokenCapture.CleanupFiles();

                if (!string.IsNullOrEmpty(token))
                {
                    _sessionService.EventsToken = token;
                    _sessionService.EventsTokenObtainedAt = DateTime.UtcNow;
                    PersistCachedToken();
                    _logger.LogInformation("Successfully captured events token.");
                    return token;
                }

                // If launched by us or still running, retry once more with a 20s capture
                if (Process.GetProcessesByName(ProcessNames.Solitaire).Length > 0)
                {
                    progressCallback?.Invoke("Retrying capture (flip a card in Solitaire)...");
                    token = EtwTokenCapture.Capture(20);
                    if (!string.IsNullOrEmpty(token))
                    {
                        _sessionService.EventsToken = token;
                        _sessionService.EventsTokenObtainedAt = DateTime.UtcNow;
                        PersistCachedToken();
                        _logger.LogInformation("Successfully captured events token on retry.");
                        return token;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while grabbing events token");
                return null;
            }
        });
    }
}
