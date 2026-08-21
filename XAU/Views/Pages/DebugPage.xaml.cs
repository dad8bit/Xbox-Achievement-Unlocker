using Wpf.Ui.Controls;
using AchievementForge.ViewModels.Pages;

namespace AchievementForge.Views.Pages
{
    public partial class DebugPage : INavigableView<DebugViewModel>
    {
        public DebugViewModel ViewModel { get; }

        public DebugPage(DebugViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
