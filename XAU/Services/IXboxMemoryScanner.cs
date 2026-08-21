namespace XAU.Services;

public interface IXboxMemoryScanner
{
    bool OpenXboxAppProcess();
    int GetXboxAppProcessId();
    Task<string?> ScanXauthFromXboxAppAsync();
}
