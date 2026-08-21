namespace AchievementForge.Services;

public interface IXboxMemoryScanner
{
    bool OpenXboxAppProcess();
    int GetXboxAppProcessId();
    Task<string?> ScanXauthFromXboxAppAsync();
}
