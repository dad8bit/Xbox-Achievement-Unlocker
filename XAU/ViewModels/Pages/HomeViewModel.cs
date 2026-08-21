using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.Services;

namespace XAU.ViewModels.Pages
{
    public partial class ImageItem : ObservableObject
    {
        [ObservableProperty]
        private string _imageUrl = string.Empty;
    }

    public partial class HomeViewModel : ObservableObject, INavigationAware
    {
        public static string ToolVersion = "26.06.14";
        public static string EventsVersion = "1.0";

        private readonly ISessionService _sessionService;
        private readonly ISettingsService _settingsService;
        private readonly IEventsTokenService _eventsTokenService;
        private readonly IXboxMemoryScanner _memoryScanner;
        private readonly IOAuthService _oauthService;
        private readonly XboxRestAPI _xboxRestAPI;
        private readonly ISnackbarService _snackbarService;
        private readonly IContentDialogService _contentDialogService;
        private readonly ILogger<HomeViewModel> _logger;

        private readonly Lazy<GithubRestApi> _gitHubRestAPI = new();
        private readonly TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);

        private CancellationTokenSource? _processLoopCts;
        private CancellationTokenSource? _eventsLoopCts;
        private bool _isInitialized;
        private bool _grabbedProfile;

        // Static compatibility accessors for other areas of the application
        public static XAUSettings Settings { get; set; } = new();
        public static string XAUTH { get => s_instance?._sessionService.XAuthToken ?? ""; set { if (s_instance != null) s_instance._sessionService.XAuthToken = value; } }
        public static string SpoofXAUTH { get => s_instance?._sessionService.SpoofXAuthToken ?? ""; set { if (s_instance != null) s_instance._sessionService.SpoofXAuthToken = value; } }
        public static string XUIDOnly { get => s_instance?._sessionService.Xuid ?? ""; set { if (s_instance != null) s_instance._sessionService.Xuid = value; } }
        public static bool InitComplete { get => s_instance?._sessionService.InitComplete ?? false; set { if (s_instance != null) s_instance._sessionService.InitComplete = value; } }
        public static bool XAUTHTested = false;
        public static int SpoofingStatus { get => s_instance?._sessionService.SpoofingStatus ?? 0; set { if (s_instance != null) s_instance._sessionService.SpoofingStatus = value; } }
        public static string SpoofedTitleID { get => s_instance?._sessionService.SpoofedTitleId ?? "0"; set { if (s_instance != null) s_instance._sessionService.SpoofedTitleId = value; } }
        public static string AutoSpoofedTitleID { get => s_instance?._sessionService.AutoSpoofedTitleId ?? "0"; set { if (s_instance != null) s_instance._sessionService.AutoSpoofedTitleId = value; } }

        public static async Task<bool> TryRefreshSpoofTokenFromXboxAppAsync()
        {
            if (s_instance == null) return false;
            try
            {
                var token = await s_instance._memoryScanner.ScanXauthFromXboxAppAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    s_instance._sessionService.SpoofXAuthToken = token;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static HomeViewModel? s_instance;

        // UI State Properties
        [ObservableProperty] private string _attached = "Not Attached";
        [ObservableProperty] private Brush _attachedColor = new SolidColorBrush(Colors.Red);
        [ObservableProperty] private string _loggedIn = "Not Logged In";
        [ObservableProperty] private Brush _loggedInColor = new SolidColorBrush(Colors.Red);

        [ObservableProperty] private string? _gamerPic = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string? _gamerTag = "Gamertag: Unknown   ";
        [ObservableProperty] private string? _xuid = "XUID: Unknown";
        [ObservableProperty] private string? _gamerScore = "Gamerscore: Unknown";
        [ObservableProperty] private string? _profileRep = "Reputation: Unknown";
        [ObservableProperty] private string? _accountTier = "Tier: Unknown";
        [ObservableProperty] private string? _currentlyPlaying = "Currently Playing: Unknown";
        [ObservableProperty] private string? _activeDevice = "Active Device: Unknown";
        [ObservableProperty] private string? _isVerified = "Verified: Unknown";
        [ObservableProperty] private string? _location = "Location: Unknown";
        [ObservableProperty] private string? _tenure = "Tenure: Unknown";
        [ObservableProperty] private string? _following = "Following: Unknown";
        [ObservableProperty] private string? _followers = "Followers: Unknown";
        [ObservableProperty] private string? _gamepass = "Gamepass: Unknown";
        [ObservableProperty] private string? _bio = "Bio: Unknown";
        [ObservableProperty] private string _loginText = "Login";
        [ObservableProperty] private bool _isLoggedIn;
        [ObservableProperty] private bool _updateAvaliable;
        [ObservableProperty] private ObservableCollection<ImageItem> _watermarks = new();

        public HomeViewModel(
            ISessionService sessionService,
            ISettingsService settingsService,
            IEventsTokenService eventsTokenService,
            IXboxMemoryScanner memoryScanner,
            IOAuthService oauthService,
            XboxRestAPI xboxRestAPI,
            ISnackbarService snackbarService,
            IContentDialogService contentDialogService,
            ILogger<HomeViewModel> logger)
        {
            _sessionService = sessionService;
            _settingsService = settingsService;
            _eventsTokenService = eventsTokenService;
            _memoryScanner = memoryScanner;
            _oauthService = oauthService;
            _xboxRestAPI = xboxRestAPI;
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;
            _logger = logger;

            s_instance = this;
            Settings = _settingsService.Current;

            _sessionService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ISessionService.IsLoggedIn))
                {
                    IsLoggedIn = _sessionService.IsLoggedIn;
                    UpdateLoginDisplay();
                }
            };
        }

        public async void OnNavigatedTo()
        {
            if (!_isInitialized)
            {
                await InitializeViewModelAsync();
            }
        }

        public void OnNavigatedFrom()
        {
        }

        private async Task InitializeViewModelAsync()
        {
            _settingsService.LoadSettings();
            Settings = _settingsService.Current;

            await CheckForToolUpdatesAsync();
            await CheckForEventUpdatesAsync();
            await CheckForXboxGamesDatabaseUpdateAsync();

            StartProcessWatchLoop();

            if (Settings.OAuthLogin)
            {
                await ExecuteOAuthLoginAsync();
            }

            if (Settings.AutoLaunchXboxAppEnabled && Process.GetProcessesByName(ProcessNames.XboxPcApp).Length == 0)
            {
                LaunchXboxApp();
            }

            _isInitialized = true;
        }

        private void LaunchXboxApp()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = @"shell:appsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"
                };

                if (Settings.LaunchHidden)
                {
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                }

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-launch Xbox App");
            }
        }

        private void StartProcessWatchLoop()
        {
            _processLoopCts?.Cancel();
            _processLoopCts = new CancellationTokenSource();
            var token = _processLoopCts.Token;

            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        var isAttached = _memoryScanner.OpenXboxAppProcess();
                        var pid = isAttached ? _memoryScanner.GetXboxAppProcessId() : 0;

                        _sessionService.IsAttached = isAttached;
                        _sessionService.AttachedProcessId = pid;

                        App.Current.Dispatcher.Invoke(() =>
                        {
                            if (isAttached || !string.IsNullOrEmpty(_sessionService.XAuthToken))
                            {
                                Attached = pid > 0 ? $"Attached to xbox app ({pid})" : "Attached";
                                AttachedColor = new SolidColorBrush(Colors.Green);
                            }
                            else
                            {
                                Attached = "Not Attached";
                                AttachedColor = new SolidColorBrush(Colors.Red);
                            }

                            UpdateLoginDisplay();
                        });

                        // If not logged in and not using OAuth, try memory scan
                        if (!_sessionService.IsLoggedIn && !Settings.OAuthLogin && isAttached)
                        {
                            var scannedToken = await _memoryScanner.ScanXauthFromXboxAppAsync();
                            if (!string.IsNullOrEmpty(scannedToken) && scannedToken != _sessionService.XAuthToken)
                            {
                                _sessionService.XAuthToken = scannedToken;
                                _sessionService.SpoofXAuthToken = scannedToken;
                                await TestXAuthAsync();
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in process watch loop");
                    }
                }
            }, token);
        }

        private void StartEventsTokenWatchLoop()
        {
            if (!Settings.AutoGrabEventsToken)
                return;

            _eventsLoopCts?.Cancel();
            _eventsLoopCts = new CancellationTokenSource();
            var token = _eventsLoopCts.Token;

            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        if (!_sessionService.IsLoggedIn || !Settings.AutoGrabEventsToken)
                            continue;

                        if (_eventsTokenService.IsTokenValid())
                            continue;

                        _logger.LogInformation("Events token expired or missing, attempting background capture...");
                        await _eventsTokenService.GrabEventsTokenAsync(launchSolitaireIfMissing: false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in events token refresh loop");
                    }
                }
            }, token);
        }

        private async Task TestXAuthAsync()
        {
            try
            {
                var response = await _xboxRestAPI.GetBasicProfileAsync();
                if (response?.ProfileUsers?.Any() == true)
                {
                    var user = response.ProfileUsers[0];
                    var xuid = user.Id ?? "";
                    var gamertag = user.Settings?.FirstOrDefault()?.Value ?? "Unknown";

                    _sessionService.SetAuthenticatedUser(xuid, gamertag, _sessionService.XAuthToken);
                    XAUTHTested = true;

                    if (Settings.PrivacyMode)
                    {
                        GamerTag = "Gamertag: Hidden";
                        Xuid = "XUID: Hidden";
                    }
                    else
                    {
                        GamerTag = $"Gamertag: {gamertag}";
                        Xuid = $"XUID: {xuid}";
                    }

                    if (!_grabbedProfile)
                    {
                        await GrabProfileAsync();
                    }

                    StartEventsTokenWatchLoop();
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _sessionService.IsLoggedIn = false;
                XAUTHTested = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TestXAuth failed");
            }
        }

        [RelayCommand]
        private async Task OAuthLogin()
        {
            await ExecuteOAuthLoginAsync();
        }

        private async Task ExecuteOAuthLoginAsync()
        {
            if (LoginText == "Logout")
            {
                _oauthService.Signout();
                _sessionService.ClearSession();
                ClearProfileState();
                LoginText = "Login";
                return;
            }

            Settings.OAuthLogin = true;
            _settingsService.SaveSettings();

            var result = await _oauthService.TryRestoreSessionAsync();
            if (result != null)
            {
                _snackbarService.Show("Success", "Logged in with previous session", ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                ApplyAuthResult(result);
                return;
            }

            result = await _oauthService.AuthenticateInteractivelyAsync(this);
            if (result != null)
            {
                _snackbarService.Show("Success", "Logged in", ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                ApplyAuthResult(result);
            }
            else
            {
                _snackbarService.Show("Error", "Failed to authenticate", ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        private void ApplyAuthResult(AuthTokensResult result)
        {
            _sessionService.SetAuthenticatedUser(result.Xuid, result.Gamertag, result.XauthToken, result.SpoofToken);
            XAUTHTested = true;

            if (Settings.PrivacyMode)
            {
                GamerTag = "Gamertag: Hidden";
                Xuid = "XUID: Hidden";
            }
            else
            {
                GamerTag = $"Gamertag: {result.Gamertag}";
                Xuid = $"XUID: {result.Xuid}";
            }

            LoginText = "Logout";
            UpdateLoginDisplay();

            if (!_grabbedProfile)
            {
                _ = GrabProfileAsync();
            }

            StartEventsTokenWatchLoop();
        }

        [RelayCommand]
        private async Task RefreshProfile()
        {
            await GrabProfileAsync();
        }

        private async Task GrabProfileAsync()
        {
            if (string.IsNullOrEmpty(_sessionService.Xuid))
                return;

            try
            {
                var profileResponse = await _xboxRestAPI.GetProfileAsync(_sessionService.Xuid);
                if (profileResponse?.People?.Any() != true)
                {
                    _snackbarService.Show("Error", "Failed to grab profile information.", ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var person = profileResponse.People.First();
                if (Settings.PrivacyMode)
                {
                    GamerTag = "Gamertag: Hidden";
                    Xuid = "XUID: Hidden";
                    GamerPic = "pack://application:,,,/Assets/cirno.png";
                    GamerScore = "Gamerscore: Hidden";
                    ProfileRep = "Reputation: Hidden";
                    AccountTier = "Tier: Hidden";
                    CurrentlyPlaying = "Currently Playing: Hidden";
                    ActiveDevice = "Active Device: Hidden";
                    IsVerified = "Verified: Hidden";
                    Location = "Location: Hidden";
                    Tenure = "Tenure: Hidden";
                    Following = "Following: Hidden";
                    Followers = "Followers: Hidden";
                    Gamepass = "Gamepass: Hidden";
                    Bio = "Bio: Hidden";
                }
                else
                {
                    GamerTag = $"Gamertag: {person?.Gamertag ?? "Unknown"}";
                    Xuid = $"XUID: {person?.Xuid ?? "Unknown"}";
                    GamerPic = (person?.DisplayPicRaw?.Replace("&mode=Padding", "")) ?? "pack://application:,,,/Assets/default.png";
                    GamerScore = $"Gamerscore: {person?.GamerScore ?? "Unknown"}";
                    ProfileRep = $"Reputation: {person?.XboxOneRep ?? "Unknown"}";
                    AccountTier = $"Tier: {person?.Detail?.AccountTier ?? "Unknown"}";

                    var presence = person?.PresenceDetails?.FirstOrDefault();
                    if (presence?.TitleId == null)
                    {
                        CurrentlyPlaying = "Currently Playing: Unknown (No Presence)";
                    }
                    else
                    {
                        var gameTitle = await _xboxRestAPI.GetGameTitleAsync(_sessionService.Xuid, presence.TitleId);
                        CurrentlyPlaying = gameTitle?.Titles?.FirstOrDefault()?.Name ?? $"Currently Playing: Unknown ({presence.TitleId})";
                    }

                    try
                    {
                        var gpuResponse = await _xboxRestAPI.GetGamepassMembershipAsync(_sessionService.Xuid);
                        Gamepass = $"Gamepass: {gpuResponse?.GamepassMembership ?? gpuResponse?.Data?.GamepassMembership ?? "Unknown"}";
                    }
                    catch
                    {
                        Gamepass = "Gamepass: Unknown";
                    }

                    ActiveDevice = $"Active Device: {presence?.Device ?? "Unknown"}";

                    if (person?.Detail != null)
                    {
                        IsVerified = $"Verified: {person.Detail.IsVerified}";
                        Location = $"Location: {person.Detail.Location ?? "Unknown"}";
                        Tenure = $"Tenure: {person.Detail.Tenure ?? "Unknown"}";
                        Following = $"Following: {person.Detail.FollowingCount}";
                        Followers = $"Followers: {person.Detail.FollowerCount}";
                        Bio = $"Bio: {person.Detail.Bio ?? "No Bio"}";

                        Watermarks.Clear();
                        if (int.TryParse(person.Detail.Tenure, out var tenureInt))
                        {
                            var tenureBadge = tenureInt.ToString("D2");
                            Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}tenure/{tenureBadge}.png" });
                        }

                        if (person.Detail.Watermarks != null)
                        {
                            foreach (var watermark in person.Detail.Watermarks)
                            {
                                Watermarks.Add(new ImageItem { ImageUrl = $@"{BasicXboxAPIUris.WatermarksUrl}launch/{watermark.ToLower()}.png" });
                            }
                        }
                    }
                }

                _grabbedProfile = true;
                _snackbarService.Show("Success", "Profile information grabbed.", ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _sessionService.IsLoggedIn = false;
                XAUTHTested = true;
                _snackbarService.Show("401 Unauthorized", "Authentication token is invalid.", ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to grab profile information. " + ex.Message, ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        private void UpdateLoginDisplay()
        {
            if (_sessionService.IsLoggedIn)
            {
                LoggedIn = "Logged In";
                LoggedInColor = new SolidColorBrush(Colors.Green);
            }
            else
            {
                LoggedIn = "Not Logged In";
                LoggedInColor = new SolidColorBrush(Colors.Red);
            }
        }

        private void ClearProfileState()
        {
            _grabbedProfile = false;
            GamerTag = "Gamertag: Unknown   ";
            Xuid = "XUID: Unknown";
            GamerPic = "pack://application:,,,/Assets/cirno.png";
            GamerScore = "Gamerscore: Unknown";
            ProfileRep = "Reputation: Unknown";
            AccountTier = "Tier: Unknown";
            CurrentlyPlaying = "Currently Playing: Unknown";
            ActiveDevice = "Active Device: Unknown";
            IsVerified = "Verified: Unknown";
            Location = "Location: Unknown";
            Tenure = "Tenure: Unknown";
            Following = "Following: Unknown";
            Followers = "Followers: Unknown";
            Gamepass = "Gamepass: Unknown";
            Bio = "Bio: Unknown";
            Watermarks.Clear();
            UpdateLoginDisplay();
        }

        public bool ManualScanRunning { get; private set; }

        public async void ScanForEventsTokenManual()
        {
            ManualScanRunning = true;
            try
            {
                await _eventsTokenService.GrabEventsTokenAsync(launchSolitaireIfMissing: true);
            }
            finally
            {
                ManualScanRunning = false;
            }
        }

        public static bool IsEventsTokenValid()
        {
            return s_instance?._eventsTokenService.IsTokenValid() ?? false;
        }

        public static DateTime? EventsTokenObtainedAtUtc =>
            s_instance?._sessionService.EventsTokenObtainedAt == DateTime.MinValue
                ? null
                : s_instance?._sessionService.EventsTokenObtainedAt;

        public static DateTime? EventsTokenExpiresAtUtc =>
            EventsTokenObtainedAtUtc.HasValue
                ? EventsTokenObtainedAtUtc.Value.AddHours(23)
                : null;

        public static bool IsUserLoggedIn => s_instance?._sessionService.IsLoggedIn ?? false;

        public static bool IsEventsTokenExpired()
        {
            return s_instance?._sessionService.IsEventsTokenExpired() ?? true;
        }

        public static void EventsLog(string msg)
        {
            s_instance?._logger.LogInformation("[EventsLog] {Message}", msg);
        }

        public void PersistEventsToken()
        {
            _eventsTokenService.PersistCachedToken();
        }

        #region Updates Check

        private async Task CheckForToolUpdatesAsync()
        {
            if (ToolVersion == "EmptyDevToolVersion")
                return;

            try
            {
                if (ToolVersion.Contains("DEV"))
                {
                    var jsonResponse = await _gitHubRestAPI.Value.GetDevToolVersionAsync();
                    if (jsonResponse != null && ("DEV-" + jsonResponse.LatestBuildVersion) != ToolVersion)
                    {
                        var result = await _contentDialogService.ShowSimpleDialogAsync(
                            new SimpleContentDialogCreateOptions
                            {
                                Title = $"Version {jsonResponse.LatestBuildVersion} available to download",
                                Content = "Would you like to update to this version?",
                                PrimaryButtonText = "Update",
                                CloseButtonText = "Cancel"
                            });

                        if (result == ContentDialogResult.Primary)
                        {
                            _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info,
                                new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                            var fileDownloader = new FileDownloader();
                            await fileDownloader.DownloadFileAsync(jsonResponse.DownloadURL.ToString(), "XAU-new.exe", UpdateTool);
                        }
                    }
                }
                else
                {
                    var jsonResponse = await _gitHubRestAPI.Value.GetReleaseVersionAsync();
                    if (jsonResponse.Count > 0 && jsonResponse[0].tag_name.ToString() != ToolVersion)
                    {
                        var result = await _contentDialogService.ShowSimpleDialogAsync(
                            new SimpleContentDialogCreateOptions
                            {
                                Title = $"Version {jsonResponse[0].tag_name} available to download",
                                Content = "Would you like to update to this version?",
                                PrimaryButtonText = "Update",
                                CloseButtonText = "Cancel"
                            });

                        if (result == ContentDialogResult.Primary)
                        {
                            _snackbarService.Show("Downloading update...", "Please wait", ControlAppearance.Info,
                                new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                            string sourceFile = (string)jsonResponse[0].assets[0].browser_download_url;
                            var fileDownloader = new FileDownloader();
                            await fileDownloader.DownloadFileAsync(sourceFile, "XAU-new.exe", UpdateTool);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for tool updates");
            }
        }

        private void UpdateTool(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            var path = Environment.ProcessPath ?? "XAU.exe";
            var splitpath = path.Split('\\');
            var exeName = splitpath.Last();

            using (var writer = new StreamWriter("XAU-Updater.bat"))
            {
                writer.WriteLine("@echo off");
                writer.WriteLine("timeout 1 > nul");
                writer.WriteLine($"del \"{path}\"");
                writer.WriteLine($"del \"{exeName}\"");
                writer.WriteLine($"ren XAU-new.exe \"{exeName}\"");
                writer.WriteLine($"start \"\" \"{exeName}\"");
                writer.WriteLine("goto 2 > nul & del \"%~f0\"");
            }

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "XAU-Updater.bat",
                    WorkingDirectory = Environment.CurrentDirectory
                }
            };
            proc.Start();
            Environment.Exit(0);
        }

        private async Task CheckForEventUpdatesAsync()
        {
            if (EventsVersion == "EmptyDevEventsVersion")
                return;

            try
            {
                var response = await _gitHubRestAPI.Value.CheckForEventUpdatesAsync();
                if (response == null) return;

                var eventsMetaFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "Events", "meta.json");

                var eventsTimestamp = 0;
                if (File.Exists(eventsMetaFilePath))
                {
                    var metaJson = File.ReadAllText(eventsMetaFilePath);
                    var meta = JsonConvert.DeserializeObject<EventsUpdateResponse>(metaJson);
                    eventsTimestamp = meta?.Timestamp ?? 0;
                }

                if (response.Timestamp > eventsTimestamp && response.DataVersion == EventsVersion)
                {
                    _snackbarService.Show("Downloading Events Update...", "Please wait", ControlAppearance.Info,
                        new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                    await UpdateEventsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for event updates");
            }
        }

        private async Task UpdateEventsAsync()
        {
            var xauPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU");
            var backupFolderPath = Path.Combine(xauPath, "Events", "Backup");
            var eventsFolderPath = Path.Combine(xauPath, "Events");

            Directory.CreateDirectory(backupFolderPath);

            if (Directory.Exists(eventsFolderPath))
            {
                foreach (var file in Directory.GetFiles(backupFolderPath))
                {
                    File.Delete(file);
                }

                foreach (var eventFile in Directory.GetFiles(eventsFolderPath))
                {
                    var fileName = Path.GetFileName(eventFile);
                    var dest = Path.Combine(backupFolderPath, fileName);
                    File.Move(eventFile, dest, true);
                }
            }

            var zipFilePath = Path.Combine(xauPath, "Events.zip");
            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(EventsUrls.Zip, zipFilePath);
            }

            ZipFile.ExtractToDirectory(zipFilePath, xauPath);
            File.Delete(zipFilePath);

            var metaFilePath = Path.Combine(eventsFolderPath, "meta.json");
            using (var client = new FileDownloader())
            {
                await client.DownloadFileAsync(EventsUrls.MetaUrl, metaFilePath);
            }

            _snackbarService.Show("Events Update Complete", "Events have been updated to the latest version.",
                ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
        }

        private async Task CheckForXboxGamesDatabaseUpdateAsync()
        {
            try
            {
                var fileInfo = await _gitHubRestAPI.Value.GetXboxGamesDatabaseInfoAsync();
                if (fileInfo == null) return;

                var titleSearchPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "TitleSearch");
                var shaFilePath = Path.Combine(titleSearchPath, "xbox_games_sha.txt");
                var dbFilePath = Path.Combine(titleSearchPath, "xbox_games.db");

                Directory.CreateDirectory(titleSearchPath);

                var currentSha = string.Empty;
                if (File.Exists(shaFilePath))
                {
                    currentSha = (await File.ReadAllTextAsync(shaFilePath)).Trim();
                }

                if (string.IsNullOrEmpty(currentSha) || !currentSha.Equals(fileInfo.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    _snackbarService.Show("Database Update", "New Xbox games database available. Downloading...",
                        ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);

                    using var client = new HttpClient();
                    var response = await client.GetAsync(fileInfo.DownloadUrl);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(dbFilePath, content);
                    await File.WriteAllTextAsync(shaFilePath, fileInfo.Sha);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for Xbox games database update");
            }
        }

        #endregion
    }
}
