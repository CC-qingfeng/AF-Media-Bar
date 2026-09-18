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
        private readonly AppLogService _log;
        private readonly MemoryPruneCoordinator _pruneCoordinator;

        public AboutPage(
            AboutViewModel viewModel,
            SettingsPersistenceService persistence,
            AppLogService log,
            MemoryPruneCoordinator pruneCoordinator)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            _log = log;
            _pruneCoordinator = pruneCoordinator;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();

        /// <summary>打开日志目录：日志只有一个文件，出问题时用户把这一份发出来即可。/ Opens the log directory; the log is one file and that one file is what the user sends when something breaks.</summary>
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => _log.OpenFolder();

        /// <summary>
        /// 手动把工作集交还给系统。
        /// Returns the working set to the system on request.
        ///
        /// 这里刻意不做任何反馈动画或提示：回收在后台线程执行，而且它只改变"物理内存占用"这一个读数，提交量不变——按钮旁边的说明已经把
        /// 这一点写清楚了，再补一个"已完成"的气泡只会让人以为释放了更多东西。
        /// No feedback animation or toast is shown on purpose: the reclaim runs on a background thread and only changes one reading — physical memory in
        /// use — while the commit size stays the same, which the description next to the button already says, and a "done" balloon would only suggest
        /// that more had been released.
        /// </summary>
        private void TrimMemory_Click(object sender, RoutedEventArgs e) =>
            _pruneCoordinator.RequestTrim(MemoryTrimTrigger.ManualRequest);

        private async void ResetAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 本页没有属于自己的设置，因此只提供“重置全部”，不再提供一个语义含糊的“本页默认”。
            // This page owns no settings of its own, so it offers only a full reset instead of an ambiguous
            // "this page's defaults".
            if (await SettingsResetDialog.ConfirmAsync("About.ResetScope")) ViewModel.ResetAll();
        }
    }
}
