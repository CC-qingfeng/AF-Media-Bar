using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// 应用与关于页：应用信息、尚未接入的功能、设置文件入口、项目信息与支持入口。
    /// 它接替了原先独立的“常规”页，因为那一页只剩下三个不可用条目和一个设置文件入口，
    /// 单独占一个导航项并不值得。
    /// Application and about page: application information, not-yet-wired features, settings-file entry points,
    /// project information, and support. It replaces the former standalone general page, which had been reduced
    /// to three unusable entries and one settings-file entry and no longer earned its own navigation item.
    /// </summary>
    public partial class AboutPage : INavigableView<AboutViewModel>
    {
        public AboutViewModel ViewModel { get; }

        private readonly SettingsPersistenceService _persistence;

        public AboutPage(AboutViewModel viewModel, SettingsPersistenceService persistence)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();

        private async void ResetAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 本页没有属于自己的设置，因此只提供“全部重置”，不再提供一个语义含糊的“本页默认”。
            // This page owns no settings of its own, so it offers only a full reset instead of an ambiguous
            // "this page's defaults".
            if (await SettingsResetDialog.ConfirmAsync("全部设置")) ViewModel.ResetAll();
        }
    }
}
