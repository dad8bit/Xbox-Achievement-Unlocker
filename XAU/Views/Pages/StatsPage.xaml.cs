using Wpf.Ui.Controls;
using AchievementForge.ViewModels.Pages;

namespace AchievementForge.Views.Pages
{
    /// <summary>
    /// Interaction logic for StatsPage.xaml
    /// </summary>
    public partial class StatsPage : INavigableView<StatsViewModel>
    {
        public StatsViewModel ViewModel { get; }

        public StatsPage(StatsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
