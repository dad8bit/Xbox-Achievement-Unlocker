using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.Services;
using XAU.Views.Pages;

namespace XAU.ViewModels.Pages
{
    public partial class GamesViewModel : ObservableObject, INavigationAware, INotifyPropertyChanged
    {
        [ObservableProperty] private string _xuidOverride = "0";
        [ObservableProperty] private ObservableCollection<Game> _games = new ObservableCollection<Game>();
        [ObservableProperty] private ObservableCollection<Game> _gamesPaged = new ObservableCollection<Game>();
        [ObservableProperty] private string _searchLabel = "Search 0 Games";
        [ObservableProperty] private GridLength _gamesListHeight = new GridLength(0, GridUnitType.Star);
        [ObservableProperty] private GridLength _loadingHeight = new GridLength(1, GridUnitType.Star);
        [ObservableProperty] private double _loadingSize = 200;
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private List<string> _filterOptions = new List<string>
        {
            "All",
            "Incomplete (< 100%)",
            "Completed (100%)",
            "Unplayed (0%)",
            "Xbox One / Series",
            "PC",
            "Xbox 360",
            "Win32"
        };
        [ObservableProperty] private int _filterIndex = 0;
        [ObservableProperty] private List<string> _sortOptions = new List<string>
        {
            "Default (Recent)",
            "Title (A → Z)",
            "Title (Z → A)",
            "Progress (Highest → Lowest)",
            "Progress (Lowest → Highest)",
            "Gamerscore (Highest → Lowest)",
            "Gamerscore (Lowest → Highest)"
        };
        [ObservableProperty] private int _sortIndex = 0;
        [ObservableProperty] private int _numPages = 0;
        [ObservableProperty] private ObservableCollection<string> _pageOptions = new ObservableCollection<string>();
        [ObservableProperty] private int _currentPage = 0;
        [ObservableProperty] private bool _isInitialized = false;

        private TitlesList GamesResponse = new TitlesList();
        public bool PageReset = true;

        public class Game
        {
            public required string Title { get; set; }
            public required string Image { get; set; }
            public required string Gamerscore { get; set; }
            public required string CurrentAchievements { get; set; }
            public required string Progress { get; set; }
            public required string Index { get; set; }
            public double NumericProgress { get; set; }
            public int NumericGamerscore { get; set; }
        }

        private readonly ISessionService _sessionService;
        private readonly XboxRestAPI _xboxRestAPI;
        private readonly ISnackbarService _snackbarService;
        private readonly INavigationService _navigationService;
        private readonly TimeSpan _snackbarDuration = TimeSpan.FromSeconds(2);

        public GamesViewModel(
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
            if (!IsInitialized && _sessionService.InitComplete)
                await InitializeViewModel();
        }

        public void OnNavigatedFrom()
        {
        }

        private async Task InitializeViewModel()
        {
            XuidOverride = _sessionService.Xuid;
            IsInitialized = true;
            await GetGamesList();
        }

        [RelayCommand]
        private async Task GetGamesList()
        {
            if (string.IsNullOrWhiteSpace(XuidOverride) || string.IsNullOrEmpty(XuidOverride))
            {
                _snackbarService.Show(
                    "Error",
                    "XUID Override cannot be empty.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    _snackbarDuration
                );
                return;
            }

            Games.Clear();
            GamesPaged.Clear();
            LoadingStart();
            GamesResponse = await _xboxRestAPI.GetGamesListAsync(XuidOverride) ?? new TitlesList();
            ApplyFilterAndSort();
        }

        partial void OnSearchTextChanged(string value)
        {
            if (IsInitialized && GamesResponse.Titles.Count > 0)
            {
                ApplyFilterAndSort();
            }
        }

        partial void OnFilterIndexChanged(int value)
        {
            if (IsInitialized && GamesResponse.Titles.Count > 0)
            {
                ApplyFilterAndSort();
            }
        }

        partial void OnSortIndexChanged(int value)
        {
            if (IsInitialized && GamesResponse.Titles.Count > 0)
            {
                ApplyFilterAndSort();
            }
        }

        public async Task OpenAchievements(string index)
        {
            if (int.TryParse(index, out var parsedIndex) && parsedIndex >= 0 && parsedIndex < GamesResponse.Titles.Count)
            {
                var title = GamesResponse.Titles[parsedIndex];
                AchievementsViewModel.TitleID = title.TitleId;
                AchievementsViewModel.IsSelectedGame360 = title.Devices.Contains("Xbox360") || title.Devices.Contains("Mobile");
                AchievementsViewModel.NewGame = true;
                _navigationService.Navigate(typeof(AchievementsPage));
            }
            await Task.CompletedTask;
        }

        [RelayCommand]
        public void SearchAndFilterGames()
        {
            ApplyFilterAndSort();
        }

        [RelayCommand]
        public void FilterGames()
        {
            ApplyFilterAndSort();
        }

        public void ApplyFilterAndSort()
        {
            if (!IsInitialized || GamesResponse?.Titles == null)
            {
                return;
            }

            LoadingStart();
            Games.Clear();
            GamesPaged.Clear();

            var query = GamesResponse.Titles.Select((title, index) => new { Title = title, Index = index });

            // 1. Text Search Filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                query = query.Where(item => item.Title.Name != null && item.Title.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Category / Platform Filter
            switch (FilterIndex)
            {
                case 1: // Incomplete (< 100%)
                    query = query.Where(item => item.Title.Achievement != null && item.Title.Achievement.ProgressPercentage < 100);
                    break;
                case 2: // Completed (100%)
                    query = query.Where(item => item.Title.Achievement != null && item.Title.Achievement.ProgressPercentage >= 100);
                    break;
                case 3: // Unplayed (0%)
                    query = query.Where(item => item.Title.Achievement == null || item.Title.Achievement.ProgressPercentage <= 0);
                    break;
                case 4: // Xbox One / Series
                    query = query.Where(item => item.Title.Devices.Contains("XboxSeries") || item.Title.Devices.Contains("XboxOne"));
                    break;
                case 5: // PC
                    query = query.Where(item => item.Title.Devices.Contains("PC"));
                    break;
                case 6: // Xbox 360
                    query = query.Where(item => item.Title.Devices.Contains("Xbox360"));
                    break;
                case 7: // Win32
                    query = query.Where(item => item.Title.Devices.Contains("Win32"));
                    break;
                default: // All
                    break;
            }

            // 3. Sorting
            switch (SortIndex)
            {
                case 1: // Title (A → Z)
                    query = query.OrderBy(item => item.Title.Name, StringComparer.OrdinalIgnoreCase);
                    break;
                case 2: // Title (Z → A)
                    query = query.OrderByDescending(item => item.Title.Name, StringComparer.OrdinalIgnoreCase);
                    break;
                case 3: // Progress (Highest → Lowest)
                    query = query.OrderByDescending(item => item.Title.Achievement?.ProgressPercentage ?? 0);
                    break;
                case 4: // Progress (Lowest → Highest)
                    query = query.OrderBy(item => item.Title.Achievement?.ProgressPercentage ?? 0);
                    break;
                case 5: // Gamerscore (Highest → Lowest)
                    query = query.OrderByDescending(item => item.Title.Achievement?.CurrentGamerscore ?? 0);
                    break;
                case 6: // Gamerscore (Lowest → Highest)
                    query = query.OrderBy(item => item.Title.Achievement?.CurrentGamerscore ?? 0);
                    break;
                default: // Default (Recent / Original list order)
                    break;
            }

            var gameList = new List<Game>();
            foreach (var item in query)
            {
                var title = item.Title;
                var currentGamerscore = title.Achievement?.CurrentGamerscore ?? 0;
                var totalGamerscore = title.Achievement?.TotalGamerscore ?? 0;
                var currentAchievements = title.Achievement?.CurrentAchievements ?? 0;
                var totalAchievements = title.Achievement?.TotalAchievements ?? 0;
                var progress = title.Achievement?.ProgressPercentage ?? 0;

                var displayImage = title.DisplayImage ?? "pack://application:,,,/Assets/cirno.png";

                gameList.Add(new Game
                {
                    Title = title.Name ?? "Unknown",
                    Image = displayImage,
                    Gamerscore = $"{currentGamerscore}/{totalGamerscore}",
                    CurrentAchievements = $"{currentAchievements}/{totalAchievements}",
                    Progress = progress.ToString(),
                    Index = item.Index.ToString(),
                    NumericProgress = progress,
                    NumericGamerscore = currentGamerscore
                });
            }

            foreach (var g in gameList)
            {
                Games.Add(g);
            }

            LoadingEnd();
            SearchLabel = $"Search {GamesResponse.Titles.Count} Games ({Games.Count} shown)";

            if (Games.Count == 0)
            {
                NumPages = 0;
                PageOptions.Clear();
                return;
            }

            NumPages = (int)Math.Ceiling(Games.Count / 252.0);
            PageReset = true;
            PageOptions.Clear();
            for (int i = 1; i <= NumPages; i++)
            {
                PageOptions.Add(i.ToString());
            }

            CurrentPage = 0;
            UpdatePagedGames();
        }

        public void UpdatePagedGames()
        {
            GamesPaged.Clear();
            var startIndex = 252 * CurrentPage;
            var endIndex = Math.Min(Games.Count, 252 * (CurrentPage + 1));
            for (int i = startIndex; i < endIndex; i++)
            {
                GamesPaged.Add(Games[i]);
            }
        }

        public void PageChanged()
        {
            if (PageReset)
            {
                PageReset = false;
                return;
            }
            UpdatePagedGames();
        }

        public void CopyToClipboard(string index)
        {
            if (int.TryParse(index, out var parsedIndex) && parsedIndex >= 0 && parsedIndex < GamesResponse.Titles.Count)
            {
                var title = GamesResponse.Titles[parsedIndex];
                try
                {
                    System.Windows.Clipboard.SetText(title.TitleId);
                    _snackbarService.Show(
                        "Title ID Copied",
                        $"Copied {title.Name} TitleID ({title.TitleId}) to clipboard.",
                        ControlAppearance.Success,
                        new SymbolIcon(SymbolRegular.Checkmark24),
                        _snackbarDuration);
                }
                catch { }
            }
        }

        public void SetPage(int page)
        {
            if (page < 0 || page >= NumPages) return;
            CurrentPage = page;
            UpdatePagedGames();
        }

        private void LoadingStart()
        {
            GamesListHeight = new GridLength(0, GridUnitType.Star);
            LoadingHeight = new GridLength(1, GridUnitType.Star);
            LoadingSize = 200;
        }

        private void LoadingEnd()
        {
            GamesListHeight = new GridLength(1, GridUnitType.Star);
            LoadingHeight = new GridLength(0, GridUnitType.Star);
            LoadingSize = 0;
        }
    }
}
