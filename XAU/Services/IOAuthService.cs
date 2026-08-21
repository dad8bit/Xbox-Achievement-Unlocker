namespace AchievementForge.Services;

public record AuthTokensResult(
    string XauthToken,
    string SpoofToken,
    string Xuid,
    string Gamertag
);

public interface IOAuthService
{
    Task<AuthTokensResult?> TryRestoreSessionAsync();
    Task<AuthTokensResult?> AuthenticateInteractivelyAsync(object? uiParent = null);
    void Signout();
}
