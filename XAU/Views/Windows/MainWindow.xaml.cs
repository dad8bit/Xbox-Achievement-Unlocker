using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using XAU.Services;
using XAU.ViewModels.Windows;

namespace XAU.Views.Windows;

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
                XAU.ViewModels.Pages.SettingsViewModel.ApplyWindowBackdrop(settings.BackdropType);
            if (!string.IsNullOrEmpty(settings.AccentColor))
                XAU.ViewModels.Pages.SettingsViewModel.ApplyAccentColor(settings.AccentColor);
            if (!string.IsNullOrEmpty(settings.ThemeMode))
                XAU.ViewModels.Pages.SettingsViewModel.ApplyThemeMode(settings.ThemeMode);
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
