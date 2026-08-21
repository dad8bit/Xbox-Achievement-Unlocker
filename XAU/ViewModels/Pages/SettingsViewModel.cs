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
                EventsUserHash = _sessionService.EventsUserHash
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
