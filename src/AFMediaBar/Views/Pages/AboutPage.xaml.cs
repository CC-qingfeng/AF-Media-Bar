using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// AboutPage.xaml 的交互逻辑
    /// </summary>
    public partial class AboutPage : INavigableView<AboutViewModel>
    {
        public AboutViewModel ViewModel { get; }

        public AboutPage(AboutViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("全部设置")) ViewModel.ResetAll();
        }
    }
}
