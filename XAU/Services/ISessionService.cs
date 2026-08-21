using System.ComponentModel;

namespace XAU.Services;

public interface ISessionService : INotifyPropertyChanged
{
    string XAuthToken { get; set; }
    string SpoofXAuthToken { get; set; }
    string EffectiveSpoofToken { get; }
    string Xuid { get; set; }
    string Gamertag { get; set; }
    bool IsLoggedIn { get; set; }
    bool IsAttached { get; set; }
    int AttachedProcessId { get; set; }
    bool InitComplete { get; set; }

    int SpoofingStatus { get; set; }
    string SpoofedTitleId { get; set; }
    string AutoSpoofedTitleId { get; set; }

    string? EventsToken { get; set; }
    string? EventsUserHash { get; set; }
    DateTime EventsTokenObtainedAt { get; set; }
    bool IsEventsTokenExpired();

    void SetAuthenticatedUser(string xuid, string gamertag, string xauthToken, string? spoofToken = null);
    void ClearSession();
}
