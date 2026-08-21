using CommunityToolkit.Mvvm.ComponentModel;

namespace XAU.Services;

public partial class SessionService : ObservableObject, ISessionService
{
    private static readonly TimeSpan DefaultEventsTokenMaxAge = TimeSpan.FromHours(23);

    [ObservableProperty] private string _xAuthToken = string.Empty;
    [ObservableProperty] private string _spoofXAuthToken = string.Empty;
    [ObservableProperty] private string _xuid = string.Empty;
    [ObservableProperty] private string _gamertag = string.Empty;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isAttached;
    [ObservableProperty] private int _attachedProcessId;
    [ObservableProperty] private bool _initComplete;

    [ObservableProperty] private int _spoofingStatus; // 0 = NotSpoofing, 1 = Spoofing, 2 = AutoSpoofing
    [ObservableProperty] private string _spoofedTitleId = "0";
    [ObservableProperty] private string _autoSpoofedTitleId = "0";

    [ObservableProperty] private string? _eventsToken;
    [ObservableProperty] private string? _eventsUserHash;
    [ObservableProperty] private DateTime _eventsTokenObtainedAt = DateTime.MinValue;

    public string EffectiveSpoofToken =>
        !string.IsNullOrWhiteSpace(SpoofXAuthToken) ? SpoofXAuthToken : XAuthToken;

    public bool IsEventsTokenExpired()
    {
        if (string.IsNullOrWhiteSpace(EventsToken))
            return true;

        if (EventsTokenObtainedAt == DateTime.MinValue)
            return false;

        return DateTime.UtcNow - EventsTokenObtainedAt >= DefaultEventsTokenMaxAge;
    }

    public void SetAuthenticatedUser(string xuid, string gamertag, string xauthToken, string? spoofToken = null)
    {
        Xuid = xuid;
        Gamertag = gamertag;
        XAuthToken = xauthToken;
        SpoofXAuthToken = spoofToken ?? xauthToken;
        IsLoggedIn = true;
        InitComplete = true;
    }

    public void ClearSession()
    {
        XAuthToken = string.Empty;
        SpoofXAuthToken = string.Empty;
        Xuid = string.Empty;
        Gamertag = string.Empty;
        IsLoggedIn = false;
        InitComplete = false;
        SpoofingStatus = 0;
        SpoofedTitleId = "0";
        AutoSpoofedTitleId = "0";
    }
}
