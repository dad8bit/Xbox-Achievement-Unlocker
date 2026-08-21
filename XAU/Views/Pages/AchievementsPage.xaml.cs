using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Controls;
using XAU.ViewModels.Pages;

namespace XAU.Views.Pages
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

        private ScrollViewer? _dataGridScrollViewer;

        private void AchievementsDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_dataGridScrollViewer == null && sender is DependencyObject dep)
            {
                _dataGridScrollViewer = FindVisualChild<ScrollViewer>(dep);
            }

            if (_dataGridScrollViewer != null)
            {
                _dataGridScrollViewer.ScrollToVerticalOffset(_dataGridScrollViewer.VerticalOffset - (e.Delta * 0.75));
                e.Handled = true;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }
    }
}
