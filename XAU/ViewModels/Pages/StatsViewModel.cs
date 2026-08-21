using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.Services;

namespace XAU.ViewModels.Pages
{
    public partial class MasteredGameItem : ObservableObject
    {
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _titleId = "";
        [ObservableProperty] private string _displayImage = "";
        [ObservableProperty] private string _gamerscoreText = "";
        [ObservableProperty] private string _achievementsText = "";
        [ObservableProperty] private string _platform = "Xbox";
    }

    public partial class InProgressGameItem : ObservableObject
    {
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _titleId = "";
        [ObservableProperty] private string _displayImage = "";
        [ObservableProperty] private double _progressPercentage;
        [ObservableProperty] private string _progressText = "";
        [ObservableProperty] private string _gamerscoreText = "";
        [ObservableProperty] private string _remainingAchievementsText = "";
    }

    public partial class StatsViewModel : ObservableObject, INavigationAware
    {
        private readonly ISessionService _sessionService;
        private readonly XboxRestAPI _xboxRestAPI;
        private readonly ISnackbarService _snackbarService;
        private readonly INavigationService _navigationService;

        private bool _isInitialized = false;

        // KPI Summary Properties
        [ObservableProperty] private string _totalGamerscore = "0 G";
        [ObservableProperty] private string _totalGamerscorePossible = "0 G";
        [ObservableProperty] private string _totalAchievementsUnlocked = "0";
        [ObservableProperty] private string _totalAchievementsPossible = "0";
        [ObservableProperty] private string _totalGamesPlayed = "0";
        [ObservableProperty] private string _masteredGamesCount = "0";
        [ObservableProperty] private string _masteredGamesPercent = "0%";
        [ObservableProperty] private string _averageCompletionRate = "0%";
        [ObservableProperty] private double _averageCompletionProgress = 0;

        // Platform Breakdowns
        [ObservableProperty] private string _consoleGamesCount = "0";
        [ObservableProperty] private string _consoleGamerscore = "0 G";
        [ObservableProperty] private string _pcGamesCount = "0";
        [ObservableProperty] private string _pcGamerscore = "0 G";
        [ObservableProperty] private string _xbox360GamesCount = "0";
        [ObservableProperty] private string _xbox360Gamerscore = "0 G";

        // State & Collections
        [ObservableProperty] private bool _isLoading = false;
        [ObservableProperty] private ObservableCollection<MasteredGameItem> _masteredGames = new();
        [ObservableProperty] private ObservableCollection<InProgressGameItem> _inProgressGames = new();

        public StatsViewModel(
            ISessionService sessionService,
            XboxRestAPI xboxRestAPI,
            ISnackbarService snackbarService,
            INavigationService navigationService)
        {
            _sessionService = sessionService;
            _xboxRestAPI = xboxRestAPI;
            _snackbarService = snackbarService;
            _navigationService = navigationService;
        }

        public async void OnNavigatedTo()
        {
            if (!_isInitialized && _sessionService.InitComplete)
            {
                await InitializeViewModel();
            }
        }

        public void OnNavigatedFrom() { }

        private async Task InitializeViewModel()
        {
            _isInitialized = true;
            await LoadStatsAsync();
        }

        [RelayCommand]
        public async Task LoadStatsAsync()
        {
            if (string.IsNullOrWhiteSpace(_sessionService.Xuid) || _sessionService.Xuid == "0")
            {
                _snackbarService.Show("Not Logged In", "Please login on the Home tab first.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Person24), TimeSpan.FromSeconds(2));
                return;
            }

            IsLoading = true;
            try
            {
                var titlesList = await _xboxRestAPI.GetGamesListAsync(_sessionService.Xuid);
                if (titlesList?.Titles == null || titlesList.Titles.Count == 0)
                {
                    IsLoading = false;
                    return;
                }

                long totalGs = 0;
                long maxGs = 0;
                long totalAch = 0;
                long maxAch = 0;
                int mastered = 0;
                double totalProgressSum = 0;
                int playedGamesCount = 0;

                int consoleCount = 0;
                long consoleGs = 0;
                int pcCount = 0;
                long pcGs = 0;
                int x360Count = 0;
                long x360Gs = 0;

                MasteredGames.Clear();
                InProgressGames.Clear();

                var masteredTemp = new List<MasteredGameItem>();
                var inProgressTemp = new List<InProgressGameItem>();

                foreach (var title in titlesList.Titles)
                {
                    if (title.Achievement == null) continue;

                    var ach = title.Achievement;
                    totalGs += ach.CurrentGamerscore;
                    maxGs += ach.TotalGamerscore;
                    totalAch += ach.CurrentAchievements;
                    maxAch += ach.TotalAchievements;

                    if (ach.TotalAchievements > 0)
                    {
                        playedGamesCount++;
                        totalProgressSum += ach.ProgressPercentage;

                        bool isComplete = ach.ProgressPercentage >= 100 || (ach.CurrentAchievements == ach.TotalAchievements && ach.TotalAchievements > 0);
                        if (isComplete)
                        {
                            mastered++;
                            string platform = "Xbox";
                            if (title.Devices != null)
                            {
                                if (title.Devices.Contains("PC") || title.Devices.Contains("Win32")) platform = "PC";
                                else if (title.Devices.Contains("Xbox360")) platform = "Xbox 360";
                                else if (title.Devices.Contains("XboxSeries") || title.Devices.Contains("XboxOne")) platform = "Xbox Series/One";
                            }

                            masteredTemp.Add(new MasteredGameItem
                            {
                                Title = title.Name ?? "Unknown Game",
                                TitleId = title.TitleId ?? "0",
                                DisplayImage = title.DisplayImage ?? "pack://application:,,,/Assets/default.png",
                                GamerscoreText = $"{ach.CurrentGamerscore} / {ach.TotalGamerscore} G",
                                AchievementsText = $"{ach.CurrentAchievements} / {ach.TotalAchievements} Achievements",
                                Platform = platform
                            });
                        }
                        else if (ach.ProgressPercentage > 0)
                        {
                            int remaining = Math.Max(0, ach.TotalAchievements - ach.CurrentAchievements);
                            inProgressTemp.Add(new InProgressGameItem
                            {
                                Title = title.Name ?? "Unknown Game",
                                TitleId = title.TitleId ?? "0",
                                DisplayImage = title.DisplayImage ?? "pack://application:,,,/Assets/default.png",
                                ProgressPercentage = ach.ProgressPercentage,
                                ProgressText = $"{ach.ProgressPercentage:F0}%",
                                GamerscoreText = $"{ach.CurrentGamerscore} / {ach.TotalGamerscore} G",
                                RemainingAchievementsText = $"{remaining} achievements left"
                            });
                        }
                    }

                    // Platform distribution
                    if (title.Devices != null)
                    {
                        if (title.Devices.Contains("PC") || title.Devices.Contains("Win32"))
                        {
                            pcCount++;
                            pcGs += ach.CurrentGamerscore;
                        }
                        else if (title.Devices.Contains("Xbox360"))
                        {
                            x360Count++;
                            x360Gs += ach.CurrentGamerscore;
                        }
                        else
                        {
                            consoleCount++;
                            consoleGs += ach.CurrentGamerscore;
                        }
                    }
                    else
                    {
                        consoleCount++;
                        consoleGs += ach.CurrentGamerscore;
                    }
                }

                TotalGamerscore = $"{totalGs:N0} G";
                TotalGamerscorePossible = $"of {maxGs:N0} G total";
                TotalAchievementsUnlocked = $"{totalAch:N0}";
                TotalAchievementsPossible = $"of {maxAch:N0} total";
                TotalGamesPlayed = $"{playedGamesCount:N0}";
                MasteredGamesCount = $"{mastered:N0}";
                MasteredGamesPercent = playedGamesCount > 0 ? $"{(mastered / (double)playedGamesCount) * 100:F1}% of library" : "0%";

                double avgProgress = playedGamesCount > 0 ? (totalProgressSum / playedGamesCount) : 0;
                AverageCompletionRate = $"{avgProgress:F1}%";
                AverageCompletionProgress = avgProgress;

                ConsoleGamesCount = $"{consoleCount} games";
                ConsoleGamerscore = $"{consoleGs:N0} G";
                PcGamesCount = $"{pcCount} games";
                PcGamerscore = $"{pcGs:N0} G";
                Xbox360GamesCount = $"{x360Count} games";
                Xbox360Gamerscore = $"{x360Gs:N0} G";

                // Populate collections
                foreach (var item in masteredTemp)
                {
                    MasteredGames.Add(item);
                }

                // In-progress sorted by nearest to completion
                foreach (var item in inProgressTemp.OrderByDescending(i => i.ProgressPercentage).Take(30))
                {
                    InProgressGames.Add(item);
                }
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Could not load stats: " + ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
