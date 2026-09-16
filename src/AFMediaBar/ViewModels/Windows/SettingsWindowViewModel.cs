using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// 设置窗口的纯数据视图模型：窗口标题，以及"是否有可用更新"这一条导航提示。
    ///
    /// 这里只发布布尔与文本：导航项上的徽章是 WPF-UI 控件，视图模型不引用控件类型，由窗口自己按这里的取值
    /// 安装或移除徽章。
    /// Pure-data view model for the settings window: the window title plus the single navigation hint telling whether
    /// an update is available.
    ///
    /// Only booleans and text are published: the badge on the navigation item is a WPF-UI control, and the view model
    /// never references control types, so the window installs or removes the badge according to these values.
    /// </summary>
    public partial class SettingsWindowViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;

        [ObservableProperty]
        private string _applicationTitle = "AFMediaBar";

        [ObservableProperty]
        private bool _hasUpdateAvailable;

        [ObservableProperty]
        private string _updateNoticeText = string.Empty;

        /// <summary>导航项上的短提示；为空时窗口隐藏该行。/ Short notice shown on the navigation item; the window hides the row when it is empty.</summary>
        [ObservableProperty]
        private string _updateNoticeTitle = string.Empty;

        /// <summary>
        /// 创建设置窗口视图模型并订阅更新状态。窗口是 Transient、视图模型是 Singleton，因此由窗口负责退订。
        /// Creates the settings-window view model and subscribes to update state. The window is transient while the
        /// view model is a singleton, so unsubscribing is the window's responsibility.
        /// </summary>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        public SettingsWindowViewModel(UpdateService updateService)
        {
            _updateService = updateService;
        }

        /// <summary>开始订阅更新状态；窗口显示时调用，可重复调用。/ Starts listening to update state, called when the window is shown and safe to call repeatedly.</summary>
        public void Subscribe() => _updateService.UpdateStateChanged += ApplyState;

        /// <summary>停止订阅更新状态；窗口关闭时调用，可重复调用。/ Stops listening to update state, called when the window closes and safe to call repeatedly.</summary>
        public void Unsubscribe() => _updateService.UpdateStateChanged -= ApplyState;

        /// <summary>按当前状态刷新提示文本，供窗口显示时立即取一次值。/ Refreshes the notice from the current state so the window can read it as soon as it is shown.</summary>
        public void Refresh() => ApplyState(_updateService.CurrentState);

        private void ApplyState(UpdateState state)
        {
            HasUpdateAvailable = state.Phase is UpdatePhase.Available
                or UpdatePhase.Downloading
                or UpdatePhase.Verifying
                or UpdatePhase.Ready
                or UpdatePhase.ManualOnly;

            UpdateNoticeText = UpdatePresentationPolicy.ResolveStatusText(
                state,
                SettingsManager.Current.Update.LastCheckUtc,
                DateTimeOffset.Now);
            UpdateNoticeTitle = UpdatePresentationPolicy.ResolveNavigationNoticeText(state);
        }
    }
}
