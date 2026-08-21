using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace XAU.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UpdateService> _logger;

        private const string RepoOwner = "dad8bit";
        private const string RepoName = "Xbox-Achievement-Unlocker";

        public UpdateService(IHttpClientFactory httpClientFactory, ILogger<UpdateService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<AppUpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", "XAU-AutoUpdater/1.0");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GitHub release check returned status: {Status}", response.StatusCode);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                string tagName = json["tag_name"]?.ToString() ?? "0.0.0";
                string releaseTitle = json["name"]?.ToString() ?? tagName;
                string releaseNotes = json["body"]?.ToString() ?? "";
                DateTime publishedAt = json["published_at"] != null ? DateTime.Parse(json["published_at"]!.ToString()) : DateTime.Now;

                // Find zip asset
                string downloadUrl = "";
                long assetSize = 0;
                var assets = json["assets"] as JArray;
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        string name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                            assetSize = asset["size"]?.ToObject<long>() ?? 0;
                            break;
                        }
                    }
                }

                var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
                string cleanTag = tagName.TrimStart('v', 'V');
                Version? remoteVer;
                if (!Version.TryParse(cleanTag, out remoteVer))
                {
                    // Fallback to major.minor
                    remoteVer = new Version(1, 0, 0);
                }

                bool isNewer = remoteVer > currentVer;

                return new AppUpdateInfo
                {
                    IsUpdateAvailable = isNewer,
                    CurrentVersion = $"{currentVer.Major}.{currentVer.Minor}.{currentVer.Build}",
                    LatestVersion = cleanTag,
                    ReleaseTitle = releaseTitle,
                    ReleaseNotes = releaseNotes,
                    DownloadUrl = downloadUrl,
                    AssetSize = assetSize,
                    PublishedAt = publishedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for app updates");
                return null;
            }
        }

        public async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return false;

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", "XAU-AutoUpdater/1.0");

                string tempZipPath = Path.Combine(Path.GetTempPath(), $"XAU_Update_{Guid.NewGuid():N}.zip");
                string extractTempDir = Path.Combine(Path.GetTempPath(), $"XAU_Extracted_{Guid.NewGuid():N}");

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes > 0)
                            {
                                progress?.Report((totalRead / (double)totalBytes) * 100.0);
                            }
                        }
                    }
                }

                // Extract downloaded zip
                Directory.CreateDirectory(extractTempDir);
                ZipFile.ExtractToDirectory(tempZipPath, extractTempDir, overwriteFiles: true);

                // Find XAU installation directory & exe
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(appDir, "AF.exe");
                int currentPid = Process.GetCurrentProcess().Id;

                // Create update batch script
                string batchScript = Path.Combine(Path.GetTempPath(), $"xau_apply_update_{Guid.NewGuid():N}.bat");
                var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /F /PID {currentPid} >nul 2>&1
timeout /t 1 /nobreak >nul
xcopy ""{extractTempDir}\*"" ""{appDir}"" /E /H /Y /Q >nul 2>&1
start """" ""{exePath}""
del ""{tempZipPath}"" >nul 2>&1
rmdir /S /Q ""{extractTempDir}"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
                await File.WriteAllTextAsync(batchScript, scriptContent);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading and applying update");
                return false;
            }
        }
    }
}
