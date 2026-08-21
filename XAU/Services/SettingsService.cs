using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace XAU.Services;

public partial class SettingsService : ObservableObject, ISettingsService
{
    private readonly ILogger<SettingsService> _logger;

    public string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "settings.json");

    [ObservableProperty] private XAUSettings _current = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            var xauDir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(xauDir))
            {
                Directory.CreateDirectory(xauDir);
            }

            if (!File.Exists(SettingsFilePath))
            {
                Current = CreateDefaultSettings();
                SaveSettings();
                return;
            }

            var settingsJson = File.ReadAllText(SettingsFilePath);
            var settings = JsonConvert.DeserializeObject<XAUSettings>(settingsJson);
            if (settings != null)
            {
                Current = settings;
            }
            else
            {
                Current = CreateDefaultSettings();
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}", SettingsFilePath);
            Current = CreateDefaultSettings();
        }
    }

    public void SaveSettings(XAUSettings? settings = null)
    {
        try
        {
            if (settings != null)
            {
                Current = settings;
            }

            var xauDir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(xauDir))
            {
                Directory.CreateDirectory(xauDir);
            }

            var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", SettingsFilePath);
        }
    }

    private static XAUSettings CreateDefaultSettings()
    {
        return new XAUSettings
        {
            SettingsVersion = "2",
            ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "26.06.14",
            UnlockAllEnabled = false,
            AutoSpooferEnabled = false,
            AutoLaunchXboxAppEnabled = false,
            LaunchHidden = false,
            FakeSignatureEnabled = true,
            RegionOverride = false,
            UseAcrylic = false,
            PrivacyMode = false,
            OAuthLogin = false,
            AutoGrabEventsToken = false
        };
    }
}
