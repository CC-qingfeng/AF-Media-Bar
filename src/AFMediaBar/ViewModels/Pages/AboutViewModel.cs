using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 关于页面的视图模型：应用信息、更新状态与更新设置。
    ///
    /// 更新状态全部来自 <see cref="UpdateService"/> 发布的不可变快照，因此这里既不需要实现重试，也不需要缓存
    /// 上一次的结论：服务说正在下载 45%，这一页显示的就是 45%，托盘菜单显示的也是同一个数字。
    /// View model for the About page: application information, update state, and update settings.
    ///
    /// Every piece of update state comes from the immutable snapshots published by <see cref="UpdateService"/>, so
    /// this page implements neither retries nor caching of the previous conclusion: when the service says 45%, this
    /// page shows 45% and the tray menu shows the same number.
    /// </summary>
    public partial class AboutViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;

        [ObservableProperty]
        private string _currentVersion = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _channelText = string.Empty;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private bool _isProgressVisible;

        [ObservableProperty]
        private bool _hasUpdate;

        [ObservableProperty]
        private bool _hasHighlights;

        /// <summary>
        /// 是否在更新区之外单独显示一条状态说明（检查本身失败、没有清单可展示时）。
        /// Whether a standalone status callout is shown outside the update block, which happens when the check itself
        /// failed and there is no manifest to display.
        /// </summary>
        [ObservableProperty]
        private bool _showStatusNotice;

        [ObservableProperty]
        private string _highlightTitle = string.Empty;

        [ObservableProperty]
        private string _highlightDate = string.Empty;

        [ObservableProperty]
        private bool _canCheck = true;

        [ObservableProperty]
        private bool _canDownload;

        [ObservableProperty]
        private bool _canCancel;

        [ObservableProperty]
        private bool _canInstallNow;

        [ObservableProperty]
        private bool _canSkip;

        [ObservableProperty]
        private bool _canOpenDownloadPage;

        [ObservableProperty]
        private bool _isPortable;

        /// <summary>当前是否确实记录着"已跳过某个版本"，用于决定"取消跳过"是否可点击。/ Whether a skipped version is actually recorded, which decides if "clear skip" is clickable.</summary>
        [ObservableProperty]
        private bool _hasSkippedVersion;

        /// <summary>
        /// 更新亮点条目（清单的 <c>changelog</c>）。
        /// Update highlight entries, taken from the manifest's <c>changelog</c>.
        /// </summary>
        public ObservableCollection<string> HighlightEntries { get; } = [];

        /// <summary>
        /// 创建关于页视图模型并订阅更新状态。两者都是单例，因此订阅与进程同寿命。
        /// Creates the About view model and subscribes to update state. Both are singletons, so the subscription
        /// lives as long as the process.
        /// </summary>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        public AboutViewModel(UpdateService updateService)
        {
            _updateService = updateService;
            CurrentVersion = updateService.CurrentVersion;
            _updateService.UpdateStateChanged += ApplyState;
            ApplyState(_updateService.CurrentState);
        }

        /// <summary>自动检查更新开关；与设置文件双向同步。/ Automatic update check toggle, synchronized with the settings file.</summary>
        public bool AutoCheckEnabled
        {
            get => SettingsManager.Current.Update.AutoCheckEnabled;
            set
            {
                if (SettingsManager.Current.Update.AutoCheckEnabled == value)
                {
                    return;
                }

                SettingsManager.Current.Update = SettingsManager.Current.Update with { AutoCheckEnabled = value };
                OnPropertyChanged();
            }
        }

        /// <summary>立即检查更新（忽略"每天一次"的间隔）。/ Checks for updates immediately, ignoring the once-a-day interval.</summary>
        [RelayCommand]
        private void CheckForUpdates() => _ = _updateService.CheckAsync(manual: true);

        /// <summary>开始下载已发现的更新。/ Starts downloading the discovered update.</summary>
        [RelayCommand]
        private void DownloadUpdate() => _updateService.StartDownload();

        /// <summary>取消正在进行的下载或校验。/ Cancels the in-flight download or verification.</summary>
        [RelayCommand]
        private void CancelDownload() => _updateService.CancelDownload();

        /// <summary>立即重启并安装：退出后由安装程序静默安装并自动启动新版本。/ Restarts and installs now: the installer runs silently after exit and starts the new version.</summary>
        [RelayCommand]
        private void InstallAndRestart() => _updateService.RequestInstallAndExit();

        /// <summary>跳过清单中的当前版本。/ Skips the version offered by the manifest.</summary>
        [RelayCommand]
        private void SkipVersion() => _updateService.SkipCurrentVersion();

        /// <summary>打开 GitHub 的下载页。/ Opens the GitHub download page.</summary>
        [RelayCommand]
        private void OpenDownloadPage() => _updateService.OpenManualDownloadPage(useAccelerator: false);

        /// <summary>通过加速站点打开下载页。/ Opens the download page through an accelerator.</summary>
        [RelayCommand]
        private void OpenAcceleratedDownloadPage() => _updateService.OpenManualDownloadPage(useAccelerator: true);

        /// <summary>清除"已跳过"的版本，使可用版本重新出现。/ Clears the skipped version so the offer comes back.</summary>
        [RelayCommand]
        private void ClearSkippedVersion() =>
            SettingsManager.Current.Update = SettingsManager.Current.Update with { SkippedVersion = null };

        /// <summary>
        /// 恢复全部默认值，并刷新本页显示的两个更新开关。
        /// Restores every default and refreshes the two update toggles shown on this page.
        /// </summary>
        public void ResetAll()
        {
            SettingsManager.ResetAll();
            RefreshSettings();
        }

        /// <summary>设置被重置后刷新本页开关的显示值。/ Refreshes the toggle shown on this page after the settings were reset.</summary>
        public void RefreshSettings()
        {
            OnPropertyChanged(nameof(AutoCheckEnabled));
            ApplyState(_updateService.CurrentState);
        }

        private void ApplyState(UpdateState state)
        {
            CurrentVersion = state.CurrentVersion;

            // 判断依据是"清单里有没有一个可用版本"，而不是阶段枚举：下载失败时阶段是 Failed，
            // 早期版本把它排除在外，于是整块更新区连同状态说明一起消失，用户看到的正是"回到发现新版本之前"，
            // 而且没有任何提示。只要还有清单，就必须留在这一页上，并让状态说明条说明失败原因。
            // The criterion is "does the manifest offer a usable version" rather than the phase enum: a failed
            // download has the Failed phase, and an earlier version excluded it, so the whole update block and its
            // status callout disappeared. The user saw exactly "back to before the update was found" with no
            // explanation at all. As long as a manifest exists the page has to keep showing it, with the callout
            // stating the failure reason.
            HasUpdate = state.Manifest is not null &&
                        state.Phase is not (UpdatePhase.Idle or UpdatePhase.Checking or UpdatePhase.UpToDate);

            // 检查本身就失败时没有清单可显示，因此失败原因需要一条独立的状态行，否则同样是"什么都没有发生"。
            // When the check itself failed there is no manifest to show, so the reason needs a status row of its own;
            // otherwise that failure also looks like "nothing happened".
            ShowStatusNotice = state.Phase == UpdatePhase.Failed && !HasUpdate;

            StatusText = UpdatePresentationPolicy.ResolveStatusText(
                state,
                SettingsManager.Current.Update.LastCheckUtc,
                DateTimeOffset.Now);
            ChannelText = UpdatePresentationPolicy.ResolveChannelText(state.ActiveSource);
            ProgressPercent = state.ProgressPercent;
            IsProgressVisible = UpdatePresentationPolicy.IsProgressVisible(state);
            CanCheck = !state.IsBusy;
            CanDownload = UpdatePresentationPolicy.ResolvePrimaryAction(state) == UpdatePrimaryAction.Download;
            CanCancel = UpdatePresentationPolicy.ResolvePrimaryAction(state) == UpdatePrimaryAction.Cancel;
            CanInstallNow = UpdatePresentationPolicy.CanInstallNow(state);
            CanSkip = UpdatePresentationPolicy.CanSkipVersion(state);
            CanOpenDownloadPage = state.Phase is UpdatePhase.ManualOnly or UpdatePhase.Failed ||
                                  state.Phase == UpdatePhase.Ready && state.IsInstallBlocked;
            IsPortable = state.InstallBlockedReason == UpdateInstallPlanPolicy.PortableBlockedReason;
            HasSkippedVersion = !string.IsNullOrEmpty(SettingsManager.Current.Update.SkippedVersion);

            HighlightTitle = state.Manifest?.Title ?? (state.AvailableVersion is { } version ? $"AF Media Bar {version}" : string.Empty);
            HighlightDate = state.Manifest?.ReleaseDate is { } date ? date.ToString("yyyy-MM-dd") : string.Empty;

            HighlightEntries.Clear();
            foreach (var entry in state.Manifest?.Changelog ?? [])
            {
                HighlightEntries.Add(entry);
            }

            HasHighlights = HighlightEntries.Count > 0;
        }
    }
}
