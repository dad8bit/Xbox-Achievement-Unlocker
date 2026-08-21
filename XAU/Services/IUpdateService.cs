using System;
using System.Threading.Tasks;

namespace AchievementForge.Services
{
    public class AppUpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "1.0.0";
        public string LatestVersion { get; set; } = "1.0.0";
        public string ReleaseTitle { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long AssetSize { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public interface IUpdateService
    {
        Task<AppUpdateInfo?> CheckForUpdatesAsync();
        Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<double>? progress = null);
    }
}
