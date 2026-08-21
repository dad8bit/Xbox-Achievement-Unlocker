namespace AchievementForge.Services;

public interface IEventsTokenService
{
    Task<string?> GrabEventsTokenAsync(bool launchSolitaireIfMissing = true, Action<string>? progressCallback = null);
    void PersistCachedToken();
    void RestoreCachedToken();
    bool IsTokenValid();
}
