using System.ComponentModel;

namespace XAU.Services;

public interface ISettingsService : INotifyPropertyChanged
{
    XAUSettings Current { get; }
    string SettingsFilePath { get; }
    void LoadSettings();
    void SaveSettings(XAUSettings? settings = null);
}
