using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Controls;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Services;
using XAU.Services;
using XAU.Views.Pages;

namespace XAU.ViewModels.Pages
{
    public partial class AchievementsViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty] private bool _isInitialized = false;
        [ObservableProperty] private string _titleIDOverride = "0";
        [ObservableProperty] private bool _unlockable = false;
        [ObservableProperty] private bool _titleIDEnabled = false;
        [ObservableProperty] private ObservableCollection<OneCoreAchievementResponse> _achievements = new ObservableCollection<OneCoreAchievementResponse>();
        [ObservableProperty] private ObservableCollection<DGAchievement> _dGAchievements = new ObservableCollection<DGAchievement>();
        [ObservableProperty] public string _gameInfo = "";
        [ObservableProperty] private string _gameName = "";
        [ObservableProperty] private bool _isUnlockAllEnabled = false;
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private List<string> _filterOptions = new List<string>
        {
            "All",
            "Locked Only",
            "Unlocked Only",
            "Secret Only"
        };
        [ObservableProperty] private int _filterIndex = 0;

        [ObservableProperty] private List<string> _sortOptions = new List<string>
        {
            "Default Order",
            "Gamerscore (High → Low)",
            "Gamerscore (Low → High)",
            "Name (A → Z)",
            "Name (Z → A)",
            "Rarity (Rarest First)"
        };
        [ObservableProperty] private int _sortIndex = 0;

        public static string TitleID = "0";
        private bool IsTitleIDValid = false;
        public static bool NewGame = false;
        public static bool IsSelectedGame360;
        private AchievementsResponse AchievementResponse = new AchievementsResponse();
        private Xbox360AchievementResponse Xbox360AchievementResponse = new Xbox360AchievementResponse();
        private Dictionary<int, DGAchievement> _unlockedAchievements = new Dictionary<int, DGAchievement>();

        private GameTitle GameInfoResponse = new GameTitle();
        public static bool SpoofingUpdate = false;
        private bool IsFiltered = false;
        private bool IsEventBased = false;
        private dynamic EventsData = (dynamic)(new JObject());

        private static AchievementsViewModel? s_instance;
        public static string? EventsToken
        {
            get => s_instance?._sessionService.EventsToken;
            set { if (s_instance != null) s_instance._sessionService.EventsToken = value; }
        }

        private readonly ISessionService _sessionService;
        private readonly ISettingsService _settingsService;
        private readonly IEventsTokenService _eventsTokenService;
        private readonly XboxRestAPI _xboxRestAPI;
        private readonly IContentDialogService _contentDialogService;
        private readonly ISnackbarService _snackbarService;
        private readonly INavigationService _navigationService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);

        public AchievementsViewModel(
            ISessionService sessionService,
            ISettingsService settingsService,
            IEventsTokenService eventsTokenService,
            XboxRestAPI xboxRestAPI,
            ISnackbarService snackbarService,
            IContentDialogService contentDialogService,
            INavigationService navigationService)
        {
            _sessionService = sessionService;
            _settingsService = settingsService;
            _eventsTokenService = eventsTokenService;
            _xboxRestAPI = xboxRestAPI;
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            s_instance = this;
        }

        // Batch Unlocker properties
        [ObservableProperty] private bool _isBatchRunning = false;
        [ObservableProperty] private double _batchProgress = 0;
        [ObservableProperty] private string _batchStatusText = "";
        [ObservableProperty] private List<string> _delayPresetOptions = new List<string>
        {
            "Fast (1-3s)",
            "Realistic (15-45s)",
            "Extended (2-5m)",
            "Instant (No Delay)"
        };
        [ObservableProperty] private int _delayPresetIndex = 1;
        [ObservableProperty] private bool _autoSpoofDuringBatch = true;
        [ObservableProperty] private int _selectedCount = 0;

        public Visibility BatchRunningVisibility => IsBatchRunning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BatchNotRunningVisibility => IsBatchRunning ? Visibility.Collapsed : Visibility.Visible;

        private CancellationTokenSource? _batchCts;

        public partial class DGAchievement : ObservableObject
        {
            [ObservableProperty] private bool _isSelected;
            public int Index { get; set; }
            public int ID { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public bool IsSecret { get; set; }
            [ObservableProperty] private DateTime _dateUnlocked;
            public int Gamerscore { get; set; }
            public float RarityPercentage { get; set; }
            public string? RarityCategory { get; set; }
            [ObservableProperty] private string? _progressState;
            [ObservableProperty] private bool _isUnlockable;
        }
        public async void OnNavigatedTo()
        {
            if (HomeViewModel.Settings.AutoSpooferEnabled)
            {

                if (!GameInfoResponse.Titles.Any() && !String.IsNullOrWhiteSpace(GameInfoResponse.Xuid))
                {
                    _snackbarService.Show("Error: Game Info Response Contained No Titles", $"There were no titles returned from the API", ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                }
                else
                {
                    if (HomeViewModel.SpoofingStatus == 1)
                    {
                        if (HomeViewModel.SpoofedTitleID == TitleIDOverride)
                        {
                            GameInfo = "Manually Spoofing";
                            GameName = GameInfoResponse.Titles[0].Name;
                        }
                        else
                        {
                            GameInfo = "Spoofing Another Game";
                            GameName = GameInfoResponse.Titles[0].Name;
                        }

                    }
                    else if (HomeViewModel.SpoofingStatus == 0 && !string.IsNullOrWhiteSpace(GameInfo))
                    {
                        SpoofGame();
                    }
                }
            }

            if (IsInitialized && NewGame)
                await RefreshAchievements();
            if (TitleID != "0")
            {
                TitleIDOverride = TitleID;
                TitleID = "0";
            }
            if (HomeViewModel.InitComplete && TitleIDOverride == "0")
                TitleIDEnabled = true;
            if (!IsInitialized && HomeViewModel.InitComplete && TitleIDOverride != "0")
                InitializeViewModel();
        }

        public void OnNavigatedFrom() { }

        private async void InitializeViewModel()
        {
            if (IsSelectedGame360)
                Unlockable = false;
            await LoadGameInfo();
            await LoadAchievements();
            if (HomeViewModel.Settings.AutoSpooferEnabled)
                SpoofGame();
            TitleIDEnabled = true;
            IsInitialized = true;
            NewGame = false;
        }


        private async Task LoadGameInfo()
        {
            // Check for a valid TitleID and set overrides
            if (TitleID != "0")
            {
                TitleIDOverride = TitleID;
                TitleID = "0";
            }

            GameInfo = string.Empty;

            // Fetch game information
            var gameInfoResponse = await _xboxRestAPI.GetGameTitleAsync(_sessionService.Xuid, TitleIDOverride);
            GameInfoResponse = gameInfoResponse ?? new GameTitle();

            // Handle response validation and set properties accordingly
            if (gameInfoResponse?.Titles?.Any() != true)
            {
                GameName = "Error";
                IsTitleIDValid = false;
                return;
            }

            var gameTitle = gameInfoResponse.Titles.FirstOrDefault();
            if (gameTitle != null)
            {
                IsSelectedGame360 = gameTitle.Devices.Contains("Xbox360") || gameTitle.Devices.Contains("Mobile");
                GameName = gameTitle.Name;
                IsTitleIDValid = true;
            }
        }

        private async void SpoofGame()
        {
            if (HomeViewModel.SpoofingStatus == 1)
            {
                if (HomeViewModel.SpoofedTitleID == TitleIDOverride)
                {
                    GameInfo = "Manually Spoofing";
                    GameName = GameInfoResponse.Titles[0].Name;
                }
                else
                {
                    GameInfo = "Spoofing Another Game";
                    GameName = GameInfoResponse.Titles[0].Name;
                }
            }
            else
            {
                HomeViewModel.AutoSpoofedTitleID = TitleIDOverride;
                HomeViewModel.SpoofingStatus = 2;
                GameInfo = "Auto Spoofing";
                if (GameInfoResponse.Titles.Any())
                {
                    GameName = GameInfoResponse.Titles[0].Name;
                }

                await Spoofing();
                if (HomeViewModel.SpoofingStatus == 1)
                {
                    if (HomeViewModel.SpoofedTitleID == HomeViewModel.AutoSpoofedTitleID)
                    {
                        GameInfo = "Manually Spoofing";
                        GameName = GameInfoResponse.Titles[0].Name;
                    }
                    else
                    {
                        GameInfo = "Spoofing Another Game";
                        GameName = GameInfoResponse.Titles[0].Name;
                    }
                }
                HomeViewModel.AutoSpoofedTitleID = "0";
            }


        }

        public async Task Spoofing()
        {
            await HomeViewModel.TryRefreshSpoofTokenFromXboxAppAsync();

            var spoofResult = await _xboxRestAPI.SendSpoofAsync(_sessionService.Xuid, _sessionService.AutoSpoofedTitleId);
            if (!spoofResult.Success)
            {
                SpoofingUpdate = true;
                return;
            }

            var i = 0;
            await Task.Delay(1000);
            SpoofingUpdate = false;
            while (!SpoofingUpdate)
            {
                if (i == 300)
                {
                    var refreshResult = await _xboxRestAPI.SendSpoofAsync(_sessionService.Xuid, _sessionService.AutoSpoofedTitleId);
                    if (!refreshResult.Success)
                    {
                        SpoofingUpdate = true;
                        break;
                    }
                    i = 0;
                }
                else
                {
                    if (SpoofingUpdate)
                    {

                        break;
                    }
                    i++;
                }
                await Task.Delay(1000);
            }
        }

        private async Task LoadAchievements()
        {

            Achievements.Clear();
            DGAchievements.Clear();
            // clears unlocked achievements from dictionary
            _unlockedAchievements.Clear();
            if (!IsTitleIDValid)
                return;
            if (!IsSelectedGame360)
            {
                Unlockable = true;
                AchievementResponse = await _xboxRestAPI.GetAchievementsForTitleAsync(_sessionService.Xuid, TitleIDOverride);
                try
                {
                    if (AchievementResponse.achievements[0].progression.requirements.Any())
                    {
                        if (AchievementResponse.achievements[0].progression.requirements[0].id !=
                            StringConstants.ZeroUid)
                        {
                            Unlockable = false;
                        }
                        else
                        {
                            Unlockable = true;
                        }
                    }
                }
                catch
                {
                    _snackbarService.Show("Error: No Achievements", $"There were no achievements returned from the API", ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }
                for (int i = 0; i < AchievementResponse.achievements.Count; i++)
                {
                    //absolutely fucking dogwater event based check
                    if (AchievementResponse.achievements[i].progression.requirements.Any())
                    {
                        if (AchievementResponse.achievements[i].progression.requirements[0].id !=
                            StringConstants.ZeroUid)
                        {
                            Unlockable = false;
                            IsEventBased = true;
                        }
                        else
                        {
                            Unlockable = true;
                            IsEventBased = false;
                        }
                    }
                    var rewardnameplaceholder = "";
                    var rewarddescriptionplaceholder = "";
                    var rewardvalueplaceholder = "";
                    var rewardtypeplaceholder = "";
                    var rewardmediaAssetplaceholder = "";
                    var rewardvalueTypeplaceholder = "";
                    try
                    {
                        rewardnameplaceholder = AchievementResponse.achievements[i].rewards[0].name;
                        rewarddescriptionplaceholder = AchievementResponse.achievements[i].rewards[0].description;
                        rewardvalueplaceholder = AchievementResponse.achievements[i].rewards[0].value;
                        rewardtypeplaceholder = AchievementResponse.achievements[i].rewards[0].type;
                        //rewardmediaAssetplaceholder = AchievementResponse.achievements[i].rewards[0].mediaAsset;
                        rewardvalueTypeplaceholder = AchievementResponse.achievements[i].rewards[0].valueType;
                    }
                    catch
                    {
                        rewardnameplaceholder = "N/A";
                        rewarddescriptionplaceholder = "N/A";
                        rewardvalueplaceholder = "N/A";
                        rewardtypeplaceholder = "N/A";
                        rewardmediaAssetplaceholder = "N/A";
                        rewardvalueTypeplaceholder = "N/A";
                    }

                    var mediaAsset = new MediaAsset
                    {
                        name = AchievementResponse.achievements[i].mediaAssets[0].name,
                        type = AchievementResponse.achievements[i].mediaAssets[0].type,
                        url = AchievementResponse.achievements[i].mediaAssets[0].url
                    };
                    var titleAssociation = new TitleAssociation
                    {
                        name = AchievementResponse.achievements[i].titleAssociations[0].name,
                        id = AchievementResponse.achievements[i].titleAssociations[0].id
                    };
                    var progression = new AchievementProgression
                    {
                        timeUnlocked = AchievementResponse.achievements[i].progression.timeUnlocked
                    };
                    var rewards = new AchievementRewards
                    {
                        name = rewardnameplaceholder,
                        description = rewarddescriptionplaceholder,
                        value = rewardvalueplaceholder,
                        type = rewardtypeplaceholder,
                        mediaAsset = mediaAsset,
                        valueType = rewardvalueTypeplaceholder
                    };


                    Achievements.Add(new OneCoreAchievementResponse()
                    {
                        id = AchievementResponse.achievements[i].id,
                        serviceConfigId = AchievementResponse.achievements[i].serviceConfigId,
                        name = AchievementResponse.achievements[i].name,
                        titleAssociations = new List<TitleAssociation>() { titleAssociation },
                        progressState = AchievementResponse.achievements[i].progressState,
                        progression = progression,
                        mediaAssets = new List<MediaAsset>() { mediaAsset },
                        platforms = AchievementResponse.achievements[i].platforms,
                        isSecret = AchievementResponse.achievements[i].isSecret,
                        description = AchievementResponse.achievements[i].description,
                        lockedDescription = AchievementResponse.achievements[i].lockedDescription,
                        productId = AchievementResponse.achievements[i].productId,
                        achievementType = AchievementResponse.achievements[i].achievementType,
                        participationType = AchievementResponse.achievements[i].participationType,
                        timeWindow = AchievementResponse.achievements[i].timeWindow,
                        rewards = new List<AchievementRewards>() { rewards },
                        estimatedTime = AchievementResponse.achievements[i].estimatedTime,
                        deeplink = AchievementResponse.achievements[i].deeplink,
                        isRevoked = AchievementResponse.achievements[i].isRevoked,
                        raritycurrentCategory = AchievementResponse.achievements[i].rarity.currentCategory,
                        raritycurrentPercentage = AchievementResponse.achievements[i].rarity.currentPercentage
                    }
                    );
                }
                foreach (var achievement in Achievements)
                {
                    var gamerscore = 0;
                    if (achievement.rewards[0].type == StringConstants.Gamerscore)
                    {
                        gamerscore = int.Parse(achievement.rewards[0].value);
                    }
                    DGAchievements.Add(new DGAchievement()
                    {
                        Index = Achievements.IndexOf(achievement),
                        ID = int.Parse(achievement.id),
                        Name = achievement.name,
                        Description = achievement.description,
                        IsSecret = achievement.isSecret,
                        DateUnlocked = DateTime.Parse(achievement.progression.timeUnlocked),
                        Gamerscore = gamerscore,
                        RarityPercentage = float.Parse(achievement.raritycurrentPercentage, CultureInfo.InvariantCulture),
                        RarityCategory = achievement.raritycurrentCategory,
                        ProgressState = achievement.progressState,
                        IsUnlockable = achievement.progressState != StringConstants.Achieved && Unlockable && !IsEventBased
                    });
                }
            }
            else
            {
                Unlockable = false;
                Xbox360AchievementResponse = await _xboxRestAPI.GetAchievementsFor360TitleAsync(_sessionService.Xuid, TitleIDOverride);
                if (Xbox360AchievementResponse?.achievements.Count == 0)
                {
                    IsSelectedGame360 = false;
                    LoadAchievements();
                    return;
                }
                //cut down version of the code to display minimal information about 360 achievements
                for (int i = 0; i < Xbox360AchievementResponse?.achievements.Count; i++)
                {
                    var rewards = new AchievementRewards
                    {
                        value = Xbox360AchievementResponse.achievements[i].gamerscore.ToString(),
                        valueType = "N/a"
                    };
                    var progression = new AchievementProgression
                    {
                        timeUnlocked = Xbox360AchievementResponse.achievements[i].timeUnlocked
                    };

                    Achievements.Add(new OneCoreAchievementResponse()
                    {
                        id = Xbox360AchievementResponse.achievements[i].id.ToString(),
                        name = Xbox360AchievementResponse.achievements[i].name,
                        isSecret = Xbox360AchievementResponse.achievements[i].isSecret,
                        description = Xbox360AchievementResponse.achievements[i].description,
                        rewards = new List<AchievementRewards>() { rewards },
                        raritycurrentCategory = Xbox360AchievementResponse.achievements[i].rarity.currentCategory,
                        raritycurrentPercentage = Xbox360AchievementResponse.achievements[i].rarity.currentPercentage,
                        progression = progression
                    }
                    );
                }
                foreach (var achievement in Achievements)
                {
                    var gamerscore = 0;
                    if (achievement.rewards[0].type == "Gamerscore")
                    {
                        gamerscore = int.Parse(achievement.rewards[0].value);
                    }
                    DGAchievements.Add(new DGAchievement()
                    {
                        Index = Achievements.IndexOf(achievement),
                        ID = int.Parse(achievement.id),
                        Name = achievement.name,
                        Description = achievement.description,
                        IsSecret = achievement.isSecret,
                        DateUnlocked = DateTime.Parse(achievement.progression.timeUnlocked),
                        Gamerscore = gamerscore,
                        RarityPercentage = float.Parse(achievement.raritycurrentPercentage, CultureInfo.InvariantCulture),
                        RarityCategory = achievement.raritycurrentCategory,
                        ProgressState = achievement.progressState,
                        IsUnlockable = achievement.progressState != StringConstants.Achieved && Unlockable
                    });
                }
            }

            if (IsSelectedGame360)
            {
                _snackbarService.Show("Warning: Unsupported Game", $"This tool does not/will not support Xbox 360 titles. To unlock 360 achievements, you can try https://www.wemod.com/horizon", ControlAppearance.Caution,
                    new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                IsUnlockAllEnabled = false;

                return;
            }

            if (IsEventBased)
            {
                //Event based logic
                string DataPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\XAU\\Events\\Data.json";
                var data = JObject.Parse(File.ReadAllText(DataPath));
                JArray SupportedGamesJ = (JArray)data["SupportedTitleIDs"];
                List<int> SupportedGames = SupportedGamesJ.ToObject<List<int>>();
                if (SupportedGames.Contains(int.Parse(TitleIDOverride)))
                {
                    Unlockable = true;
                    EventsData = (dynamic)(JObject)data[TitleIDOverride];
                    foreach (var achievement in DGAchievements)
                    {
                        if (EventsData.Achievements.ContainsKey(achievement.ID.ToString()) && achievement.ProgressState != StringConstants.Achieved)
                        {
                            achievement.IsUnlockable = true;
                        }
                    }
                }
                CollectionViewSource.GetDefaultView(DGAchievements).Refresh();
            }



            if (!Unlockable)
            {
                _snackbarService.Show("Warning: Unsupported Game", $"This tool does not support this Event Based title", ControlAppearance.Caution,
                    new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
            }
            else if (IsEventBased && EventsData.FullySupported == false)
            {
                _snackbarService.Show("Warning: Partially Unsupported Game", $"This tool does not fully support this title. Not all achievements are unlockable", ControlAppearance.Caution,
                                       new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
            }

            if (HomeViewModel.Settings.UnlockAllEnabled && Unlockable && !IsEventBased)
                IsUnlockAllEnabled = Unlockable;
            else
                IsUnlockAllEnabled = false;
        }

        [RelayCommand]
        public void SelectAllLocked()
        {
            foreach (var a in DGAchievements)
            {
                if (a.ProgressState != StringConstants.Achieved && a.IsUnlockable)
                {
                    a.IsSelected = true;
                }
            }
            UpdateSelectedCount();
        }

        [RelayCommand]
        public void DeselectAll()
        {
            foreach (var a in DGAchievements)
            {
                a.IsSelected = false;
            }
            UpdateSelectedCount();
        }

        public void UpdateSelectedCount()
        {
            SelectedCount = DGAchievements.Count(a => a.IsSelected);
        }

        public async Task<bool> PerformUnlockAchievementAsync(DGAchievement achievement, bool showIndividualSnackbar = true)
        {
            if (!IsEventBased)
            {
                try
                {
                    var scid = AchievementResponse.achievements.FirstOrDefault()?.serviceConfigId ?? "";
                    var tid = AchievementResponse.achievements.FirstOrDefault()?.titleAssociations?.FirstOrDefault()?.id ?? TitleIDOverride;
                    await _xboxRestAPI.UnlockTitleBasedAchievementAsync(scid, tid, _sessionService.Xuid, achievement.ID.ToString(), _settingsService.Current.FakeSignatureEnabled);

                    achievement.IsUnlockable = false;
                    achievement.ProgressState = StringConstants.Achieved;
                    achievement.DateUnlocked = DateTime.Now;

                    if (!_unlockedAchievements.ContainsKey(achievement.ID))
                    {
                        _unlockedAchievements.Add(achievement.ID, achievement);
                    }

                    if (showIndividualSnackbar)
                    {
                        _snackbarService.Show("Achievement Unlocked", $"{achievement.Name} has been unlocked",
                            ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                    }

                    return true;
                }
                catch (HttpRequestException ex)
                {
                    if (showIndividualSnackbar)
                    {
                        _snackbarService.Show("Error: Achievement Not Unlocked",
                            $"{achievement.Name} was not unlocked: {ex.Message}", ControlAppearance.Danger,
                            new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    }
                    return false;
                }
            }
            else
            {
                if (EventsToken == null || HomeViewModel.IsEventsTokenExpired())
                {
                    if (showIndividualSnackbar)
                    {
                        _snackbarService.Show("Error", "Events token is missing or expired. Set one in Settings.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    }
                    return false;
                }

                try
                {
                    var eventFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "Events", $"{TitleIDOverride}.json");
                    if (!File.Exists(eventFilePath))
                    {
                        return false;
                    }

                    var requestbody = File.ReadAllText(eventFilePath);
                    DateTime timestamp = DateTime.UtcNow;
                    var aidKey = achievement.ID.ToString();

                    if (EventsData?.Achievements != null && EventsData.Achievements[aidKey] != null)
                    {
                        foreach (var i in EventsData.Achievements[aidKey])
                        {
                            var ReplacementData = i.Value;
                            switch (ReplacementData.ReplacementType.ToString())
                            {
                                case "Replace":
                                    requestbody = requestbody.Replace(ReplacementData.Target.ToString(), ReplacementData.Replacement.ToString());
                                    break;
                                case "RangeInt":
                                    int min = ReplacementData.Min;
                                    int max = ReplacementData.Max;
                                    int randomint = Random.Shared.Next(min, max);
                                    requestbody = requestbody.Replace(ReplacementData.Target.ToString(), randomint.ToString());
                                    break;
                                case "RangeFloat":
                                    float minF = ReplacementData.Min;
                                    float maxF = ReplacementData.Max;
                                    float randomfloat = (float)Random.Shared.NextDouble() * (maxF - minF) + minF;
                                    requestbody = requestbody.Replace(ReplacementData.Target.ToString(), randomfloat.ToString(CultureInfo.InvariantCulture));
                                    break;
                                case "StupidFuckingLDAPTimestamp":
                                    long ldapTimestamp = DateTime.Now.ToFileTime();
                                    requestbody = requestbody.Replace(ReplacementData.Target.ToString(), ldapTimestamp.ToString());
                                    break;
                            }
                        }
                    }

                    requestbody = requestbody.Replace("REPLACETIME", timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
                    requestbody = requestbody.Replace("REPLACESEQ", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                    requestbody = requestbody.Replace("REPLACEXUID", _sessionService.Xuid);
                    requestbody = JObject.Parse(requestbody).ToString(Formatting.None);
                    var bodyconverted = new StringContent(requestbody, Encoding.UTF8, "application/x-json-stream");

                    await _xboxRestAPI.UnlockEventBasedAchievement(_sessionService.EventsToken ?? "", bodyconverted);

                    achievement.IsUnlockable = false;
                    achievement.ProgressState = StringConstants.Achieved;
                    achievement.DateUnlocked = DateTime.Now;

                    if (!_unlockedAchievements.ContainsKey(achievement.ID))
                    {
                        _unlockedAchievements.Add(achievement.ID, achievement);
                    }

                    if (showIndividualSnackbar)
                    {
                        _snackbarService.Show("Achievement Unlocked", $"{achievement.Name} has been unlocked",
                            ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    if (showIndividualSnackbar)
                    {
                        _snackbarService.Show("Error: Achievement Not Unlocked",
                            $"{achievement.Name} was not unlocked: {ex.Message}", ControlAppearance.Danger,
                            new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    }
                    return false;
                }
            }
        }

        public async void UnlockAchievement(int AchievementIndex)
        {
            if (AchievementIndex >= 0 && AchievementIndex < DGAchievements.Count)
            {
                var achievement = DGAchievements[AchievementIndex];
                await PerformUnlockAchievementAsync(achievement, showIndividualSnackbar: true);
                CollectionViewSource.GetDefaultView(DGAchievements)?.Refresh();
            }
        }

        [RelayCommand]
        public async Task StartBatchUnlock()
        {
            var selected = DGAchievements.Where(a => a.IsSelected && a.ProgressState != StringConstants.Achieved && a.IsUnlockable).ToList();
            if (selected.Count == 0)
            {
                _snackbarService.Show("No Selection", "Please select one or more locked achievements to unlock.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                return;
            }

            IsBatchRunning = true;
            OnPropertyChanged(nameof(BatchRunningVisibility));
            OnPropertyChanged(nameof(BatchNotRunningVisibility));
            _batchCts = new CancellationTokenSource();
            var token = _batchCts.Token;

            if (AutoSpoofDuringBatch)
            {
                SpoofGame();
            }

            int total = selected.Count;
            int successful = 0;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var achievement = selected[i];
                    BatchProgress = (i / (double)total) * 100;
                    BatchStatusText = $"Unlocking {i + 1} of {total}: {achievement.Name}...";

                    bool ok = await PerformUnlockAchievementAsync(achievement, showIndividualSnackbar: false);
                    if (ok)
                    {
                        successful++;
                        achievement.IsSelected = false;
                    }

                    CollectionViewSource.GetDefaultView(DGAchievements)?.Refresh();

                    if (i < total - 1)
                    {
                        int delaySec = DelayPresetIndex switch
                        {
                            0 => Random.Shared.Next(1, 4),      // Fast: 1-3s
                            1 => Random.Shared.Next(15, 46),    // Realistic: 15-45s
                            2 => Random.Shared.Next(120, 301),  // Extended: 2-5m
                            _ => 1                              // Instant
                        };

                        for (int s = delaySec; s > 0; s--)
                        {
                            if (token.IsCancellationRequested) break;
                            BatchStatusText = $"Unlocked {achievement.Name} • Next in {s}s ({i + 1}/{total})";
                            await Task.Delay(1000, token);
                        }
                    }
                }

                BatchProgress = 100;
                BatchStatusText = token.IsCancellationRequested
                    ? $"Batch cancelled. Unlocked {successful} of {total}."
                    : $"Completed! Successfully unlocked {successful} of {total} achievements.";

                _snackbarService.Show("Batch Complete", BatchStatusText, ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(4));
            }
            catch (OperationCanceledException)
            {
                BatchStatusText = $"Batch cancelled by user. ({successful}/{total} unlocked)";
            }
            catch (Exception ex)
            {
                BatchStatusText = $"Batch encountered an error: {ex.Message}";
            }
            finally
            {
                IsBatchRunning = false;
                OnPropertyChanged(nameof(BatchRunningVisibility));
                OnPropertyChanged(nameof(BatchNotRunningVisibility));
                UpdateSelectedCount();
            }
        }

        [RelayCommand]
        public void CancelBatchUnlock()
        {
            if (_batchCts != null && !_batchCts.IsCancellationRequested)
            {
                _batchCts.Cancel();
                BatchStatusText = "Cancelling batch unlock...";
            }
        }

        [RelayCommand]
        public async Task UnlockAll()
        {
            var lockedAchievementIds = Achievements.Where(o => o.progressState != StringConstants.Achieved).Select(o => o.id).ToList();
            try
            {
                await _xboxRestAPI.UnlockTitleBasedAchievementsAsync(serviceConfigId: AchievementResponse.achievements[0].serviceConfigId,
                    titleId: AchievementResponse.achievements[0].titleAssociations[0].id, xuid: _sessionService.Xuid, achievementIds: lockedAchievementIds, useFakeSignature: _settingsService.Current.FakeSignatureEnabled);

                _snackbarService.Show("All Achievements Unlocked", $"All Achievements for this game have been unlocked",
                    ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                var unlocktime = DateTime.Now;
                foreach (DGAchievement achievement in DGAchievements)
                {

                    if (achievement.ProgressState != StringConstants.Achieved)
                    {
                        achievement.IsUnlockable = false;
                        achievement.ProgressState = StringConstants.Achieved;
                        achievement.DateUnlocked = unlocktime;
                    }
                }
                CollectionViewSource.GetDefaultView(DGAchievements).Refresh();
            }
            catch (HttpRequestException hre)
            {
                _snackbarService.Show("Error: Achievements Not Unlocked",
                                        $"{hre.Message}", ControlAppearance.Danger,
                                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        [RelayCommand]
        public async Task RefreshAchievements()
        {
            // clears unlocked achievements from dictionary
            _unlockedAchievements.Clear();

            await LoadGameInfo();
            await LoadAchievements();
            NewGame = false;
            if (HomeViewModel.Settings.AutoSpooferEnabled)
                SpoofGame();
        }

        partial void OnSearchTextChanged(string value)
        {
            if (IsInitialized && Achievements.Count > 0)
            {
                ApplyAchievementFilterAndSort();
            }
        }

        partial void OnFilterIndexChanged(int value)
        {
            if (IsInitialized && Achievements.Count > 0)
            {
                ApplyAchievementFilterAndSort();
            }
        }

        partial void OnSortIndexChanged(int value)
        {
            if (IsInitialized && Achievements.Count > 0)
            {
                ApplyAchievementFilterAndSort();
            }
        }

        [RelayCommand]
        public async Task SearchAndFilterAchievements()
        {
            ApplyAchievementFilterAndSort();
            await Task.CompletedTask;
        }

        public void ApplyAchievementFilterAndSort()
        {
            try
            {
                if (IsEventBased)
                {
                    string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "Events", "Data.json");
                    if (File.Exists(DataPath))
                    {
                        var data = JObject.Parse(File.ReadAllText(DataPath));
                        JArray? SupportedGamesJ = (JArray?)data["SupportedTitleIDs"];
                        List<int>? SupportedGames = SupportedGamesJ?.ToObject<List<int>>();
                        if (int.TryParse(TitleIDOverride, out var parsedTitleId) && SupportedGames != null && SupportedGames.Contains(parsedTitleId))
                        {
                            Unlockable = true;
                            EventsData = (dynamic)data[TitleIDOverride];
                        }
                    }
                }

                DGAchievements.Clear();

                var list = new List<DGAchievement>();

                for (int i = 0; i < Achievements.Count; i++)
                {
                    var achievement = Achievements[i];
                    var gamerscore = 0;
                    if (achievement.rewards.Count > 0 && achievement.rewards[0].type == StringConstants.Gamerscore)
                    {
                        _ = int.TryParse(achievement.rewards[0].value, out gamerscore);
                    }

                    _ = DateTime.TryParse(achievement.progression?.timeUnlocked, out var dateUnlocked);
                    _ = float.TryParse(achievement.raritycurrentPercentage, NumberStyles.Any, CultureInfo.InvariantCulture, out var rarity);

                    var isAchieved = achievement.progressState == StringConstants.Achieved;
                    var dgAchievement = new DGAchievement()
                    {
                        Index = i,
                        ID = int.TryParse(achievement.id, out var aid) ? aid : i,
                        Name = achievement.name,
                        Description = achievement.description,
                        IsSecret = achievement.isSecret,
                        DateUnlocked = dateUnlocked,
                        Gamerscore = gamerscore,
                        RarityPercentage = rarity,
                        RarityCategory = achievement.raritycurrentCategory,
                        ProgressState = achievement.progressState,
                        IsUnlockable = !isAchieved && Unlockable && !IsEventBased
                    };

                    if (_unlockedAchievements.TryGetValue(dgAchievement.ID, out var unlocked))
                    {
                        dgAchievement.IsUnlockable = unlocked.IsUnlockable;
                        dgAchievement.ProgressState = unlocked.ProgressState;
                        dgAchievement.DateUnlocked = unlocked.DateUnlocked;
                    }

                    list.Add(dgAchievement);
                }

                IEnumerable<DGAchievement> query = list;

                // 1. Text search
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var search = SearchText.Trim();
                    query = query.Where(a => (a.Name != null && a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                                             (a.Description != null && a.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                                             a.ID.ToString().Contains(search));
                }

                // 2. Filter
                switch (FilterIndex)
                {
                    case 1: // Locked Only
                        query = query.Where(a => a.ProgressState != StringConstants.Achieved);
                        break;
                    case 2: // Unlocked Only
                        query = query.Where(a => a.ProgressState == StringConstants.Achieved);
                        break;
                    case 3: // Secret Only
                        query = query.Where(a => a.IsSecret);
                        break;
                    default: // All
                        break;
                }

                // 3. Sort
                switch (SortIndex)
                {
                    case 1: // Gamerscore (High → Low)
                        query = query.OrderByDescending(a => a.Gamerscore);
                        break;
                    case 2: // Gamerscore (Low → High)
                        query = query.OrderBy(a => a.Gamerscore);
                        break;
                    case 3: // Name (A → Z)
                        query = query.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
                        break;
                    case 4: // Name (Z → A)
                        query = query.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase);
                        break;
                    case 5: // Rarity (Rarest First)
                        query = query.OrderBy(a => a.RarityPercentage);
                        break;
                    default: // Default (Index order)
                        query = query.OrderBy(a => a.Index);
                        break;
                }

                foreach (var a in query)
                {
                    DGAchievements.Add(a);
                }

                if (IsEventBased && Unlockable && EventsData?.Achievements != null)
                {
                    foreach (var achievement in DGAchievements)
                    {
                        if (EventsData.Achievements.ContainsKey(achievement.ID.ToString()) && achievement.ProgressState != StringConstants.Achieved)
                        {
                            achievement.IsUnlockable = true;
                        }
                    }
                }

                CollectionViewSource.GetDefaultView(DGAchievements)?.Refresh();
            }
            catch (Exception)
            {
                _snackbarService.Show("Error", "An error occurred while filtering achievements.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }
    }
}
