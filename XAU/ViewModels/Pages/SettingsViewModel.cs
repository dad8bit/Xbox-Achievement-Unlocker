using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Wpf.Ui.Controls;
using XAU.Services;
using XAU.Services.HttpServer;

namespace XAU.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware, IDisposable
    {
        private readonly ISessionService _sessionService;
        private readonly ISettingsService _settingsService;
        private readonly XboxRestAPI _xboxRestAPI;

        private bool _isInitialized;
        private HttpServer? _httpServer;
        private bool _disposed;

        [ObservableProperty] private string _appVersion = string.Empty;

        // Settings Properties
        [ObservableProperty] private string? _settingsVersion;
        [ObservableProperty] private string? _toolVersion;
        [ObservableProperty] private bool _unlockAllEnabled;
        [ObservableProperty] private bool _autoSpooferEnabled;
        [ObservableProperty] private bool _autoLaunchXboxAppEnabled;
        [ObservableProperty] private bool _launchHidden;
        [ObservableProperty] private bool _fakeSignatureEnabled;
        [ObservableProperty] private bool _regionOverride;
        [ObservableProperty] private bool _useAcrylic;
        [ObservableProperty] private bool _privacyMode;
        [ObservableProperty] private bool _oAuthLogin;
        [ObservableProperty] private bool _autoGrabEventsToken;
        [ObservableProperty] private string _xauth = string.Empty;

        [ObservableProperty] private List<string> _backdropOptions = new() { "Mica", "Tabbed", "Acrylic", "None" };
        [ObservableProperty] private int _selectedBackdropIndex = 0;
        [ObservableProperty] private List<string> _accentOptions = new() { "Xbox Green", "Neon Purple", "Cyber Blue", "Crimson Red", "Sunset Orange" };
        [ObservableProperty] private int _selectedAccentIndex = 0;
        [ObservableProperty] private List<string> _themeOptions = new() { "Dark", "Light" };
        [ObservableProperty] private int _selectedThemeIndex = 0;

        [ObservableProperty] private bool _serverEnabled;
        [ObservableProperty] private string _serverPort = "1337";
        [ObservableProperty] private string _listeningAddress = "http://localhost:1337";

        public static bool ManualXauth = false;
        public event RoutedEventHandler? OnNavigatedToEvent;

        public SettingsViewModel(
            ISessionService sessionService,
            ISettingsService settingsService,
            XboxRestAPI xboxRestAPI)
        {
            _sessionService = sessionService;
            _settingsService = settingsService;
            _xboxRestAPI = xboxRestAPI;
        }

        public void OnNavigatedTo()
        {
            if (!_isInitialized)
                InitializeViewModel();

            LoadSettings();
            OnNavigatedToEvent?.Invoke(this, new RoutedEventArgs());
        }

        public void OnNavigatedFrom()
        {
        }

        private void InitializeViewModel()
        {
            AppVersion = $"XAU - {GetAssemblyVersion()}";
            ToolVersion = $"XAU - {GetAssemblyVersion()}";
            SettingsVersion = "2";
            _isInitialized = true;

            if (_httpServer == null)
            {
                var routes = Routes.GetRoutes(
                    getXauthToken: () => _sessionService.XAuthToken,
                    getXboxRestAPI: () => _xboxRestAPI,
                    getXUIDOnly: () => _sessionService.Xuid
                );
                _httpServer = new HttpServer(ServerPort, routes);
            }
            ListeningAddress = $"http://localhost:{ServerPort}";
        }

        partial void OnSelectedBackdropIndexChanged(int value)
        {
            if (value >= 0 && value < BackdropOptions.Count)
            {
                var backdropName = BackdropOptions[value];
                _settingsService.Current.BackdropType = backdropName;
                SaveSettings();
                ApplyWindowBackdrop(backdropName);
            }
        }

        partial void OnSelectedAccentIndexChanged(int value)
        {
            if (value >= 0 && value < AccentOptions.Count)
            {
                var accentName = AccentOptions[value];
                _settingsService.Current.AccentColor = accentName;
                SaveSettings();
                ApplyAccentColor(accentName);
            }
        }

        partial void OnSelectedThemeIndexChanged(int value)
        {
            if (value >= 0 && value < ThemeOptions.Count)
            {
                var themeName = ThemeOptions[value];
                _settingsService.Current.ThemeMode = themeName;
                SaveSettings();
                ApplyThemeMode(themeName);
            }
        }

        public static void ApplyWindowBackdrop(string backdropName)
        {
            if (System.Windows.Application.Current?.MainWindow is FluentWindow win)
            {
                win.WindowBackdropType = backdropName switch
                {
                    "Tabbed" => WindowBackdropType.Tabbed,
                    "Acrylic" => WindowBackdropType.Acrylic,
                    "None" => WindowBackdropType.None,
                    _ => WindowBackdropType.Mica
                };
            }
        }

        public static void ApplyAccentColor(string accentName)
        {
            var color = accentName switch
            {
                "Neon Purple" => System.Windows.Media.Color.FromRgb(138, 43, 226),
                "Cyber Blue" => System.Windows.Media.Color.FromRgb(0, 120, 215),
                "Crimson Red" => System.Windows.Media.Color.FromRgb(232, 17, 35),
                "Sunset Orange" => System.Windows.Media.Color.FromRgb(255, 140, 0),
                _ => System.Windows.Media.Color.FromRgb(16, 124, 16) // Xbox Green
            };

            try
            {
                Wpf.Ui.Appearance.Accent.Apply(color);
            }
            catch { }
        }

        public static void ApplyThemeMode(string themeName)
        {
            try
            {
                if (themeName == "Light")
                {
                    Wpf.Ui.Appearance.Theme.Apply(Wpf.Ui.Appearance.ThemeType.Light);
                }
                else
                {
                    Wpf.Ui.Appearance.Theme.Apply(Wpf.Ui.Appearance.ThemeType.Dark);
                }
            }
            catch { }
        }

        public void LoadSettings()
        {
            var settings = _settingsService.Current;
            SettingsVersion = settings.SettingsVersion;
            ToolVersion = settings.ToolVersion;
            UnlockAllEnabled = settings.UnlockAllEnabled;
            AutoSpooferEnabled = settings.AutoSpooferEnabled;
            AutoLaunchXboxAppEnabled = settings.AutoLaunchXboxAppEnabled;
            LaunchHidden = settings.LaunchHidden;
            FakeSignatureEnabled = settings.FakeSignatureEnabled;
            RegionOverride = settings.RegionOverride;
            UseAcrylic = settings.UseAcrylic;
            PrivacyMode = settings.PrivacyMode;
            Xauth = _sessionService.XAuthToken;
            OAuthLogin = settings.OAuthLogin;
            AutoGrabEventsToken = settings.AutoGrabEventsToken;

            // Load theme preferences
            int bIdx = BackdropOptions.IndexOf(settings.BackdropType);
            SelectedBackdropIndex = bIdx >= 0 ? bIdx : 0;

            int aIdx = AccentOptions.IndexOf(settings.AccentColor);
            SelectedAccentIndex = aIdx >= 0 ? aIdx : 0;

            int tIdx = ThemeOptions.IndexOf(settings.ThemeMode);
            SelectedThemeIndex = tIdx >= 0 ? tIdx : 0;
        }

        [RelayCommand]
        public void SaveSettings()
        {
            var settings = new XAUSettings
            {
                SettingsVersion = SettingsVersion,
                ToolVersion = ToolVersion,
                UnlockAllEnabled = UnlockAllEnabled,
                AutoSpooferEnabled = AutoSpooferEnabled,
                AutoLaunchXboxAppEnabled = AutoLaunchXboxAppEnabled,
                LaunchHidden = LaunchHidden,
                FakeSignatureEnabled = FakeSignatureEnabled,
                RegionOverride = RegionOverride,
                UseAcrylic = UseAcrylic,
                PrivacyMode = PrivacyMode,
                OAuthLogin = OAuthLogin,
                AutoGrabEventsToken = AutoGrabEventsToken,
                CachedEventsToken = _sessionService.EventsToken,
                EventsTokenObtainedAt = _sessionService.EventsTokenObtainedAt,
                EventsUserHash = _sessionService.EventsUserHash,
                BackdropType = SelectedBackdropIndex >= 0 && SelectedBackdropIndex < BackdropOptions.Count ? BackdropOptions[SelectedBackdropIndex] : "Mica",
                AccentColor = SelectedAccentIndex >= 0 && SelectedAccentIndex < AccentOptions.Count ? AccentOptions[SelectedAccentIndex] : "Xbox Green",
                ThemeMode = SelectedThemeIndex >= 0 && SelectedThemeIndex < ThemeOptions.Count ? ThemeOptions[SelectedThemeIndex] : "Dark"
            };

            _settingsService.SaveSettings(settings);
            HomeViewModel.Settings = settings;
        }

        [RelayCommand]
        private void ToggleServer()
        {
            if (_httpServer == null)
            {
                var routes = Routes.GetRoutes(
                    getXauthToken: () => _sessionService.XAuthToken,
                    getXboxRestAPI: () => _xboxRestAPI,
                    getXUIDOnly: () => _sessionService.Xuid
                );
                _httpServer = new HttpServer(ServerPort, routes);
            }

            if (ServerEnabled)
            {
                _httpServer.Start();
                UpdateListeningAddress();
            }
            else
            {
                _httpServer.Stop();
                ListeningAddress = $"http://localhost:{ServerPort}";
            }
        }

        [RelayCommand]
        public void UpdateServerPort()
        {
            if (_httpServer != null)
            {
                _httpServer.UpdatePort(ServerPort);
                UpdateListeningAddress();
            }
        }

        [RelayCommand]
        public void OpenSettingsFile()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_settingsService.SettingsFilePath}\""
            });
        }

        [RelayCommand]
        public void OpenSettingsFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Path.GetDirectoryName(_settingsService.SettingsFilePath)}\""
            });
        }

        private string GetAssemblyVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
        }

        private void UpdateListeningAddress()
        {
            if (_httpServer != null)
            {
                ListeningAddress = _httpServer.GetListeningAddress();
            }
        }

        partial void OnServerPortChanged(string value)
        {
            if (_httpServer != null)
            {
                _httpServer.UpdatePort(value);
                UpdateListeningAddress();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_httpServer != null)
            {
                _httpServer.Dispose();
                _httpServer = null;
            }

            _disposed = true;
        }
    }
}
