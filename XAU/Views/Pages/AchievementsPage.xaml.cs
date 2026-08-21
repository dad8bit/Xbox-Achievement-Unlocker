using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Controls;
using AchievementForge.ViewModels.Pages;

namespace AchievementForge.Views.Pages
{
    /// <summary>
    /// Interaction logic for AchievementsPage.xaml
    /// </summary>
    public partial class AchievementsPage : INavigableView<AchievementsViewModel>
    {
        public AchievementsViewModel ViewModel { get; }

        public AchievementsPage(AchievementsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }

        private void UnlockButton(object sender, RoutedEventArgs e)
        {
            ButtonBase SelectedAchievement = sender as ButtonBase;
            ViewModel.UnlockAchievement(Convert.ToInt32(SelectedAchievement.Tag));
        }

        private void FilterBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {


        }

        private async void SearchBox_OnKeyDownAsync(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                //for some reason, the search text is not being updated when pressing enter
                ViewModel.SearchText = SearchBox.Text;
                await ViewModel.SearchAndFilterAchievements();
            }
        }

        private ScrollViewer? _parentScrollViewer;

        private void AchievementsDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_parentScrollViewer == null && sender is DependencyObject dep)
            {
                _parentScrollViewer = FindParent<ScrollViewer>(dep);
            }

            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.ScrollToVerticalOffset(_parentScrollViewer.VerticalOffset - (e.Delta * 0.75));
                e.Handled = true;
            }
            else if (sender is FrameworkElement element && element.Parent is UIElement parent)
            {
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                parent.RaiseEvent(eventArg);
                e.Handled = true;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                {
                    return typedParent;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
