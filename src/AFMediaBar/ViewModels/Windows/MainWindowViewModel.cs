using System.Diagnostics;
using System.Windows.Input;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Updates;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// MainWindow 的视图模型：宿主窗口元数据 + 任务栏右键菜单的媒体控制/设置/退出命令，
    /// 以及右键菜单里的那一行更新提示。
    /// View model for MainWindow: host window metadata plus the taskbar context menu commands
    /// (media control, settings, exit) and the single update entry in that menu.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;

        [ObservableProperty]
        private string _applicationTitle = "AFMediaBar";

        /// <summary>
        /// 右键菜单里更新那一行的标题：它随状态变化，因此下载百分比与"点击重启安装"都能在菜单里看到。
        /// Title of the update entry in the context menu. It follows the state, which is how the download percentage
        /// and the "click to restart and install" offer become visible inside the menu.
        /// </summary>
        [ObservableProperty]
        private string _updateMenuHeader = "检查更新";

        /// <summary>更新那一行当前是否可点击：检查、下载与校验期间不可点击。/ Whether the update entry is clickable: it is not during a check, download or verification.</summary>
        [ObservableProperty]
        private bool _isUpdateMenuEnabled = true;

        /// <summary>
        /// 请求宿主窗口打开设置页。
        /// Requests that the host window open the settings page.
        /// </summary>
        public event EventHandler? OpenSettingsRequested;

        /// <summary>
        /// 请求宿主窗口打开设置页的「应用与关于」：更新提示被点击时跳到那一页。
        /// Requests that the host window open "application and about", which is where the update notice takes the
        /// user when it is clicked.
        /// </summary>
        public event EventHandler? OpenUpdateSettingsRequested;

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        public ICommand SelectMediaSessionCommand { get; }

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        public ICommand ReconnectMediaSessionCommand { get; }

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>退出整个程序。/ Exits the application.</summary>
        public ICommand ExitApplicationCommand { get; }

        /// <summary>右键菜单的更新入口：按当前状态检查、取消下载、打开更新页或立即重启安装。/ The context menu's update entry: check, cancel the download, open the update page or restart and install, depending on the state.</summary>
        public ICommand UpdateMenuCommand { get; }

        /// <summary>切换当前媒体播放状态。/ Toggles playback for the selected media session.</summary>
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>播放上一首媒体。/ Skips to the previous item in the selected media session.</summary>
        public ICommand SkipPreviousCommand { get; }

        /// <summary>播放下一首媒体。/ Skips to the next item in the selected media session.</summary>
        public ICommand SkipNextCommand { get; }

        /// <summary>跳转当前媒体进度。 / Seeks the selected media item.</summary>
        public ICommand SeekCommand { get; }

        /// <summary>切换循环模式。 / Cycles the repeat mode.</summary>
        public ICommand CycleRepeatCommand { get; }

        /// <summary>激活当前媒体来源应用。/ Activates the application that owns the selected media session.</summary>
        public ICommand ActivateMediaSourceCommand { get; }

        /// <summary>
        /// 创建主窗口状态适配器，并订阅媒体服务在 UI 线程发布的快照与会话列表，以及更新状态。
        /// Creates the main-window state adapter and subscribes to snapshots and session lists published by the
        /// media service on the UI thread, plus update state.
        /// </summary>
        /// <param name="mediaSessionService">媒体会话协调器。/ Media session coordinator.</param>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        public MainWindowViewModel(MediaSessionService mediaSessionService, UpdateService updateService)
        {
            _updateService = updateService;
            SelectMediaSessionCommand = new RelayCommand<string>(key => mediaSessionService.SelectSession(key ?? string.Empty));
            ReconnectMediaSessionCommand = new AsyncRelayCommand(() => mediaSessionService.ReconnectAsync());
            OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
            ExitApplicationCommand = new RelayCommand(() => Application.Current.Shutdown());
            UpdateMenuCommand = new RelayCommand(ExecuteUpdateAction);
            TogglePlayPauseCommand = new AsyncRelayCommand(mediaSessionService.TogglePlayPauseAsync);
            SkipPreviousCommand = new AsyncRelayCommand(mediaSessionService.SkipPreviousAsync);
            SkipNextCommand = new AsyncRelayCommand(mediaSessionService.SkipNextAsync);
            SeekCommand = new AsyncRelayCommand<double>(mediaSessionService.SeekAsync);
            CycleRepeatCommand = new AsyncRelayCommand(mediaSessionService.CycleRepeatModeAsync);
            ActivateMediaSourceCommand = new RelayCommand(mediaSessionService.ActivateSelectedSource);

            // 视图模型与更新服务都是单例，订阅与进程同寿命；状态事件始终在 UI 线程上发布。
            // Both the view model and the update service are singletons, so the subscription lives as long as the
            // process; state events are always published on the UI thread.
            _updateService.UpdateStateChanged += ApplyUpdateState;
            ApplyUpdateState(_updateService.CurrentState);
        }

        private void ApplyUpdateState(UpdateState state)
        {
            UpdateMenuHeader = UpdatePresentationPolicy.ResolveTrayHeader(state);
            IsUpdateMenuEnabled = UpdatePresentationPolicy.IsTrayHeaderEnabled(state);
        }

        private void ExecuteUpdateAction()
        {
            switch (UpdatePresentationPolicy.ResolveTrayAction(_updateService.CurrentState))
            {
                case UpdateTrayAction.Check:
                    _ = CheckForUpdatesAsync();
                    break;

                case UpdateTrayAction.Cancel:
                    _updateService.CancelDownload();
                    break;

                case UpdateTrayAction.InstallAndRestart:
                    _updateService.RequestInstallAndExit();
                    break;

                case UpdateTrayAction.OpenUpdatePage:
                    OpenUpdateSettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        /// <summary>
        /// 手动检查更新；异常只写进调试输出，因为一次失败的更新绝不能让右键菜单命令崩溃。
        /// Checks for updates manually; failures only reach the debug output, because a failed update must never
        /// crash a context-menu command.
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await _updateService.CheckAsync(manual: true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[Update] Manual check failed: {exception}");
            }
        }
    }
}
