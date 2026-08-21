using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using AchievementForge.Services;
using AchievementForge.ViewModels.Windows;

namespace AchievementForge.Views.Windows;

public partial class MainWindow
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        ISettingsService settingsService
    )
    {
        Wpf.Ui.Appearance.Watcher.Watch(this);

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
        navigationService.SetNavigationControl(NavigationView);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetContentPresenter(RootContentDialog);

        NavigationView.SetServiceProvider(serviceProvider);

        try
        {
            var settings = settingsService.Current;
            if (!string.IsNullOrEmpty(settings.BackdropType))
                AchievementForge.ViewModels.Pages.SettingsViewModel.ApplyWindowBackdrop(settings.BackdropType);
            if (!string.IsNullOrEmpty(settings.AccentColor))
                AchievementForge.ViewModels.Pages.SettingsViewModel.ApplyAccentColor(settings.AccentColor);
            if (!string.IsNullOrEmpty(settings.ThemeMode))
                AchievementForge.ViewModels.Pages.SettingsViewModel.ApplyThemeMode(settings.ThemeMode);
        }
        catch { }
    }

    private void NavigationView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationView navigationView)
        {
            return;
        }

        navigationView.IsPaneOpen = false;
    }
}
