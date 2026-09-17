using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>
/// 协调托盘音频面板的设备预览、应用音量和滚轮语义。
/// Coordinates device preview, application volume, and tray-wheel semantics for the audio flyout.
/// </summary>
public partial class AudioControlViewModel : ObservableObject, IDisposable
{
    private const int VolumeStepPercent = 2;
    private static readonly TimeSpan DeviceApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds);
    private static readonly TimeSpan VolumeApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds);
    private readonly AudioDeviceService _deviceService;
    private readonly SpatialAudioService _spatialAudioService;
    private readonly ApplicationVolumeService _volumeService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly ShellTrayIconService _trayIconService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly Dictionary<string, int> _volumeApplyVersions = new(StringComparer.OrdinalIgnoreCase);
    private int _deviceApplyVersion;
    private bool _isRefreshing;
    private bool _isPreviewingOutputDevice;
    private int _pendingTrayDeviceSteps;
    private bool _isProcessingTrayDevice;
    private int _pendingTrayVolumeSteps;
    private bool _isProcessingTrayVolume;
    private int _tooltipRefreshVersion;
    private string? _tooltipSourceId;
    private string? _tooltipSourceName;
    private DateTime _suppressTrayLeftClickUntilUtc;
    private DateTime _suppressTrayContextMenuUntilUtc;
    private readonly DispatcherTimer _trayTooltipTimer;
    private WheelGestureSlot? _appliedTrayWheelSlot;
    private bool _trayWheelResultShown;
    private bool _disposed;

    public ObservableCollection<AudioDeviceOption> OutputDevices { get; } = [];
    public ObservableCollection<ApplicationVolumeItemViewModel> Applications { get; } = [];

    [ObservableProperty] private AudioDeviceOption? _selectedOutputDevice;
    [ObservableProperty] private string _spatialAudioText = "状态不可用";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public ICommand RefreshCommand { get; }
    public ICommand OpenSpatialAudioSettingsCommand { get; }

    public event Action<TrayIconBounds?>? FlyoutToggleRequested;
    public event Action<TrayIconBounds?>? TrayContextMenuRequested;
    public event EventHandler? SettingsOpenRequested;

    /// <summary>
    /// 请求在托盘图标处打开输出设备菜单。菜单本身属于任务栏宿主，因此这里只表达意图并带上锚点。
    /// Requests the output-device menu to open at the tray icon. The menu belongs to the taskbar host, so this only expresses the
    /// intent and carries the anchor.
    /// </summary>
    public event Action<TrayIconBounds?>? OutputDeviceMenuRequested;

    /// <summary>请求在托盘图标处打开当前应用音量菜单。/ Requests the current application's volume menu to open at the tray icon.</summary>
    public event Action<TrayIconBounds?>? CurrentAppVolumeMenuRequested;

    /// <summary>
    /// 创建音频面板状态协调器；设备、应用音量和提示状态均通过注入服务异步刷新。
    /// Creates the audio-panel state coordinator; devices, application volume, and tooltip state are refreshed asynchronously through injected services.
    /// </summary>
    public AudioControlViewModel(
        AudioDeviceService deviceService,
        SpatialAudioService spatialAudioService,
        ApplicationVolumeService volumeService,
        MediaSessionService mediaSessionService,
        ShellTrayIconService trayIconService,
        NativeMouseInputMonitor mouseInputMonitor,
        AudioMonitorService audioMonitorService)
    {
        _deviceService = deviceService;
        _spatialAudioService = spatialAudioService;
        _volumeService = volumeService;
        _mediaSessionService = mediaSessionService;
        _trayIconService = trayIconService;
        _mouseInputMonitor = mouseInputMonitor;
        _audioMonitorService = audioMonitorService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenSpatialAudioSettingsCommand = new RelayCommand(OpenSpatialAudioSettings);
        _trayIconService.LeftClicked += OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested += OnTrayContextMenuRequested;
        _trayIconService.TooltipOpening += OnTrayTooltipOpening;
        _mouseInputMonitor.WheelChanged += OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged += OnMediaSnapshotChanged;
        SettingsManager.InteractionSettingsChanged += OnInteractionSettingsChanged;
        // 气泡打开后按需轮询按键状态：托盘图标不提供按键事件，而"按住组合键"必须立刻反映到提示上。
        // Poll the modifier state while the bubble is open: the shell tray icon offers no key events, and "hold the chord key" has to
        // show up in the tooltip immediately.
        _trayTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _trayTooltipTimer.Tick += (_, _) => AdvanceTrayTooltipPoll();
        _mouseInputMonitor.Start();
        QueueTrayTooltipRefresh();
    }

    /// <summary>
    /// 刷新输出设备、应用音量和空间音效状态。
    /// Refreshes output devices, application volumes, and spatial-audio state.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var devices = await _deviceService.GetRenderDevicesAsync().WaitAsync(TimeSpan.FromSeconds(3));
            _isRefreshing = true;
            OutputDevices.Clear();
            foreach (var device in devices)
            {
                OutputDevices.Add(device);
            }

            SelectedOutputDevice = devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
            _isRefreshing = false;
            RefreshSpatialAudio();

            var applications = await Task.Run(() => _volumeService.GetApplications(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName)).WaitAsync(TimeSpan.FromSeconds(3));
            Applications.Clear();
            foreach (var application in applications)
            {
                Applications.Add(new ApplicationVolumeItemViewModel(application, QueueApplicationVolume));
            }

            StatusText = applications.Count == 0 ? "当前没有应用音频会话" : string.Empty;
            UpdateTooltipFromLoadedState();
        }
        catch (Exception exception)
        {
            _isRefreshing = false;
            StatusText = $"读取音频状态失败：{exception.Message}";
            Debug.WriteLine($"[AudioControlViewModel] Refresh failed: {exception}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 根据托盘滚轮增量预览下一个输出设备。
    /// Previews the next output device for a tray-wheel delta.
    /// </summary>
    /// <param name="delta">滚轮增量 / Wheel delta.</param>
    public void PreviewOutputDeviceWheel(int delta)
    {
        PreviewOutputDeviceSteps(TrayWheelPolicy.GetDeviceSteps(delta));
    }

    private void PreviewOutputDeviceSteps(int signedSteps)
    {
        if (OutputDevices.Count == 0 || signedSteps == 0)
        {
            return;
        }

        var current = Math.Max(0, SelectedOutputDevice is null ? 0 : OutputDevices.IndexOf(SelectedOutputDevice));
        var device = OutputDevices[WheelInput.MoveCircular(current, signedSteps, OutputDevices.Count)];

        _isPreviewingOutputDevice = true;
        try
        {
            SelectedOutputDevice = device;
        }
        finally
        {
            _isPreviewingOutputDevice = false;
        }

        // 单设备或循环回原设备时属性值不会变化，也必须即时刷新原生提示。
        // Refresh the native tooltip immediately even when one device or a full cycle keeps the same selection.
        SetTrayTooltip($"输出设备：{device.DisplayName}");
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceOption? value)
    {
        if (!_isRefreshing && value is not null)
        {
            StartOutputDeviceApply(
                value,
                _isPreviewingOutputDevice ? DeviceApplyDelay : TimeSpan.Zero);
        }
    }

    private void StartOutputDeviceApply(AudioDeviceOption device, TimeSpan delay)
    {
        var version = ++_deviceApplyVersion;
        _ = ApplyOutputDeviceAsync(device, version, delay);
        SetTrayTooltip($"输出设备：{device.DisplayName}");
    }

    private async Task ApplyOutputDeviceAsync(AudioDeviceOption device, int version, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            if (!AudioApplyPolicy.IsCurrent(_disposed, version, _deviceApplyVersion))
            {
                return;
            }

            // 缓冲结束后再次读取系统默认设备；循环滚回原设备时不要重复切换而打断音频。
            // Recheck the system default after buffering so cycling back does not interrupt audio with a redundant switch.
            if (_deviceService.IsDefaultRenderDevice(device.Id))
            {
                return;
            }

            await Task.Run(() => _deviceService.SetDefaultRenderDevice(device.PolicyId));
            _audioMonitorService.ResetAfterEnvironmentChange();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"切换输出设备失败：{exception.Message}";
            SetTrayTooltip("输出设备切换失败");
        }
    }

    private void QueueApplicationVolume(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        bool applyImmediately)
    {
        StartApplicationVolumeApply(
            application,
            volumePercent,
            applyImmediately ? TimeSpan.Zero : VolumeApplyDelay);
    }

    private void StartApplicationVolumeApply(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        TimeSpan delay)
    {
        var version = _volumeApplyVersions.GetValueOrDefault(application.ProcessName) + 1;
        _volumeApplyVersions[application.ProcessName] = version;
        _ = ApplyApplicationVolumeAsync(application, volumePercent, version, delay);
    }

    private async Task ApplyApplicationVolumeAsync(
        ApplicationVolumeItemViewModel application,
        int volumePercent,
        int version,
        TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            if (_disposed || !_volumeApplyVersions.TryGetValue(application.ProcessName, out var current) || current != version)
            {
                return;
            }

            await Task.Run(() => _volumeService.SetApplicationVolume(application.ProcessName, volumePercent));
        }
        catch (Exception exception)
        {
            StatusText = $"调节 {application.DisplayName} 音量失败：{exception.Message}";
        }
        finally
        {
            if (_volumeApplyVersions.TryGetValue(application.ProcessName, out var current) && current == version)
            {
                _volumeApplyVersions.Remove(application.ProcessName);
            }
        }
    }

    private void OnTrayWheelChanged(object? sender, TrayWheelEventArgs e)
    {
        var interaction = SettingsManager.Current.Interaction.Normalize();
        if (interaction.Modifier == InteractionModifier.LeftMouseButton && e.IsLeftButtonDown)
            _suppressTrayLeftClickUntilUtc = DateTime.UtcNow.AddMilliseconds(450);
        if (interaction.Modifier == InteractionModifier.RightMouseButton && e.IsRightButtonDown)
            _suppressTrayContextMenuUntilUtc = DateTime.UtcNow.AddMilliseconds(450);

        switch (GlobalWheelGesturePolicy.ResolveTray(
                    interaction,
                    e.IsShiftDown,
                    e.IsLeftButtonDown,
                    e.IsRightButtonDown))
        {
            case TrayWheelBehavior.AdjustVolume:
                _pendingTrayVolumeSteps += TrayWheelPolicy.GetVolumeSteps(e.Delta);
                _ = ProcessTrayVolumeAsync();
                break;
            case TrayWheelBehavior.SwitchOutputDevice:
                _pendingTrayDeviceSteps += TrayWheelPolicy.GetDeviceSteps(e.Delta);
                _ = ProcessTrayDeviceAsync();
                break;
        }
    }

    private async Task ProcessTrayDeviceAsync()
    {
        if (_isProcessingTrayDevice)
            return;

        _isProcessingTrayDevice = true;
        try
        {
            while (_pendingTrayDeviceSteps != 0)
            {
                if (OutputDevices.Count == 0)
                    await LoadOutputDevicesForTrayAsync();
                if (_disposed)
                    return;
                if (OutputDevices.Count == 0)
                {
                    _pendingTrayDeviceSteps = 0;
                    SetTrayTooltip("输出设备：不可用");
                    return;
                }

                var steps = _pendingTrayDeviceSteps;
                _pendingTrayDeviceSteps = 0;
                PreviewOutputDeviceSteps(steps);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tray device preview failed: {exception}");
            SetTrayTooltip("输出设备切换失败");
        }
        finally
        {
            _isProcessingTrayDevice = false;
            if (!_disposed && _pendingTrayDeviceSteps != 0)
                _ = ProcessTrayDeviceAsync();
        }
    }

    private async Task LoadOutputDevicesForTrayAsync()
    {
        var devices = await _deviceService.GetRenderDevicesAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (_disposed)
            return;

        _isRefreshing = true;
        try
        {
            OutputDevices.Clear();
            foreach (var device in devices)
                OutputDevices.Add(device);
            SelectedOutputDevice = devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task ProcessTrayVolumeAsync()
    {
        if (_isProcessingTrayVolume)
        {
            return;
        }

        _isProcessingTrayVolume = true;
        try
        {
            while (_pendingTrayVolumeSteps != 0)
            {
                var steps = _pendingTrayVolumeSteps;
                _pendingTrayVolumeSteps = 0;
                var current = await Task.Run(() => _volumeService.GetCurrentMediaVolume(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName));
                if (current is null)
                {
                    SetTrayTooltip("当前媒体音量：不可用");
                    continue;
                }

                var next = Math.Clamp(current.VolumePercent + steps * VolumeStepPercent, 0, 100);
                await Task.Run(() => _volumeService.SetApplicationVolume(current.ProcessName, next));
                Applications.FirstOrDefault(item => string.Equals(item.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase))?.SynchronizeVolume(next);
                SetTrayTooltip($"{current.DisplayName}：{next}%");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tray volume failed: {exception}");
            SetTrayTooltip("音量调节失败");
        }
        finally
        {
            _isProcessingTrayVolume = false;
            if (_pendingTrayVolumeSteps != 0)
            {
                _ = ProcessTrayVolumeAsync();
            }
        }
    }

    private void RefreshSpatialAudio()
    {
        SpatialAudioText = SelectedOutputDevice is null
            ? "无输出设备"
            : _spatialAudioService.GetState(SelectedOutputDevice.Id).DisplayName;
    }

    private void OpenSpatialAudioSettings()
    {
        try
        {
            _spatialAudioService.OpenSystemSettings();
        }
        catch (Exception exception)
        {
            StatusText = $"无法打开系统声音设置：{exception.Message}";
        }
    }

    private void OnTrayLeftClicked(object? sender, EventArgs e)
    {
        // 组合滚轮（按住鼠标键再滚动）结束时的松键会合成一次单击：用户只打算滚动，因此这次单击必须被吞掉。
        // 判定来自全局鼠标钩子，且标记是一次性的，所以无论结果如何都要取走。
        // Releasing the button after a chord wheel (scrolling while a mouse button is held) synthesizes a click, and the user only
        // meant to scroll, so it has to be swallowed. The hook makes that call and the flag is one-shot, so it is taken either way.
        var chordWheelClick = _mouseInputMonitor.ConsumeSuppressedClick();
        if (chordWheelClick || DateTime.UtcNow < _suppressTrayLeftClickUntilUtc)
            return;

        switch (SettingsManager.Current.Interaction.TrayClickAction)
        {
            case TrayClickAction.OpenSettings:
                SettingsOpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayClickAction.OpenAudioControl:
                FlyoutToggleRequested?.Invoke(GetTrayBounds());
                break;
            case TrayClickAction.OpenOutputDeviceMenu:
                OutputDeviceMenuRequested?.Invoke(GetTrayBounds());
                break;
            case TrayClickAction.OpenCurrentAppVolumeMenu:
                CurrentAppVolumeMenuRequested?.Invoke(GetTrayBounds());
                break;
            case TrayClickAction.OpenContextMenu:
                TrayContextMenuRequested?.Invoke(GetTrayBounds());
                break;
        }
    }
    private void OnTrayContextMenuRequested(object? sender, EventArgs e)
    {
        // 右键菜单同样可能是组合滚轮松键的合成结果，因此与左键点击走同一个抑制判定。
        // The context menu can equally be synthesized by releasing after a chord wheel, so it shares the left click's rule.
        var chordWheelClick = _mouseInputMonitor.ConsumeSuppressedClick();
        if (!chordWheelClick && DateTime.UtcNow >= _suppressTrayContextMenuUntilUtc)
            TrayContextMenuRequested?.Invoke(GetTrayBounds());
    }

    private void OnMediaSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (string.Equals(_tooltipSourceId, snapshot.SourceId, StringComparison.Ordinal) &&
            string.Equals(_tooltipSourceName, snapshot.SourceName, StringComparison.Ordinal))
        {
            return;
        }

        _tooltipSourceId = snapshot.SourceId;
        _tooltipSourceName = snapshot.SourceName;
        if (SettingsManager.Current.Interaction.TrayPrimaryWheelAction == TrayWheelBehavior.AdjustVolume)
            QueueTrayTooltipRefresh();
    }

    private void OnTrayTooltipOpening(object? sender, EventArgs e)
    {
        // 每次气泡打开都从提示态开始，并启动按键轮询；指针离开图标后轮询自停。
        // Every bubble starts in the hint state and starts the key poll, which stops itself once the pointer leaves the icon.
        _appliedTrayWheelSlot = null;
        _trayWheelResultShown = false;
        _trayTooltipTimer.Start();
        QueueTrayTooltipRefresh();
    }

    private void OnInteractionSettingsChanged(object? sender, EventArgs e)
    {
        QueueTrayTooltipRefresh();
    }

    private void QueueTrayTooltipRefresh()
    {
        var version = ++_tooltipRefreshVersion;
        _ = RefreshTrayTooltipAsync(version);
    }

    private async Task RefreshTrayTooltipAsync(int version)
    {
        try
        {
            var settings = SettingsManager.Current.Interaction.Normalize();
            var slot = ChordWheelHeld ? WheelGestureSlot.Chord : WheelGestureSlot.Primary;
            if (_trayWheelResultShown && slot == _appliedTrayWheelSlot)
                return;

            _appliedTrayWheelSlot = slot;
            _trayWheelResultShown = false;
            var behavior = slot == WheelGestureSlot.Chord
                ? settings.TrayChordWheelAction
                : settings.TrayPrimaryWheelAction;
            ApplicationVolumeSnapshot? application = null;
            AudioDeviceOption? device = null;
            if (behavior == TrayWheelBehavior.AdjustVolume)
            {
                application = await Task.Run(() => _volumeService.GetCurrentMediaVolume(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName));
            }
            else if (behavior == TrayWheelBehavior.SwitchOutputDevice)
            {
                var devices = await _deviceService.GetRenderDevicesAsync();
                device = devices.FirstOrDefault(candidate => candidate.IsDefault) ?? devices.FirstOrDefault();
            }

            // 悬停提示说明"滚轮现在做什么"，并把当前结果附在后面：用户既知道手势会做什么，也知道它此刻的值。
            // The hover hint states what the wheel does right now and appends the current value, so the user learns both the gesture
            // and where it currently stands.
            var hint = WheelTooltipPolicy.BuildHint(
                slot,
                settings.Modifier,
                WheelTooltipPolicy.BuildActionName(behavior));
            var currentValue = behavior == TrayWheelBehavior.AdjustVolume
                ? BuildVolumeDetail(application)
                : device?.DisplayName;
            var text = WheelTooltipPolicy.BuildHintWithValue(hint, currentValue);

            if (!_disposed && version == _tooltipRefreshVersion)
            {
                _trayIconService.UpdateTooltip(text);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AudioControlViewModel] Tooltip refresh failed: {exception.Message}");
            if (!_disposed && version == _tooltipRefreshVersion)
            {
                _trayIconService.UpdateTooltip("AF Media Bar");
            }
        }
    }

    private void UpdateTooltipFromLoadedState() => QueueTrayTooltipRefresh();

    private static string? BuildVolumeDetail(ApplicationVolumeSnapshot? application) =>
        application is null ? null : $"{application.DisplayName} {application.VolumePercent}%";

    /// <summary>
    /// 当前是否按住了共用组合键。托盘的滚轮绑定同样按这个键在普通与组合之间切换；托盘图标是 Shell 图标，
    /// 不提供按键事件，因此输入状态一律向全局鼠标监听器询问，Win32 调用留在那个服务里。
    /// Whether the shared chord key is currently held. The tray's wheel binding switches between plain and chord on the same key, and
    /// because the shell tray icon offers no key events the input state is asked of the global mouse monitor, keeping Win32 calls
    /// inside that service.
    /// </summary>
    private bool ChordWheelHeld => GlobalWheelGesturePolicy.IsChordHeld(
        SettingsManager.Current.Interaction,
        _mouseInputMonitor.IsShiftDown,
        _mouseInputMonitor.IsLeftButtonDown,
        _mouseInputMonitor.IsRightButtonDown);

    /// <summary>
    /// 托盘提示的按键轮询：气泡打开时启动，指针离开图标后自停，因此按键状态一变提示就跟着换槽位。
    /// The tray tooltip's key poll: it starts when the bubble opens and stops once the pointer leaves the icon, so a modifier change
    /// switches the hint to its own slot immediately.
    /// </summary>
    private void AdvanceTrayTooltipPoll()
    {
        if (!_mouseInputMonitor.IsPointerOverTray())
        {
            _trayTooltipTimer.Stop();
            return;
        }

        QueueTrayTooltipRefresh();
    }

    private void SetTrayTooltip(string text)
    {
        _tooltipRefreshVersion++;
        // 滚轮动作的结果留在气泡上，直到按键状态变化或指针离开；否则轮询会在 120 ms 后把结果换回提示。
        // The wheel result stays on the bubble until the modifier state changes or the pointer leaves; otherwise the poll would
        // replace it with the hint 120 ms later.
        _appliedTrayWheelSlot = ChordWheelHeld ? WheelGestureSlot.Chord : WheelGestureSlot.Primary;
        _trayWheelResultShown = true;
        _trayIconService.UpdateTooltip(text);
    }

    private TrayIconBounds? GetTrayBounds() => _trayIconService.TryGetBounds(out var bounds) ? bounds : null;

    /// <summary>
    /// 取消托盘和媒体事件订阅并释放输入监听器。
    /// Unsubscribes tray and media events and releases the input monitor.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _tooltipRefreshVersion++;
        _trayIconService.LeftClicked -= OnTrayLeftClicked;
        _trayIconService.ContextMenuRequested -= OnTrayContextMenuRequested;
        _trayIconService.TooltipOpening -= OnTrayTooltipOpening;
        _mouseInputMonitor.WheelChanged -= OnTrayWheelChanged;
        _mediaSessionService.SnapshotChanged -= OnMediaSnapshotChanged;
        SettingsManager.InteractionSettingsChanged -= OnInteractionSettingsChanged;
        _mouseInputMonitor.Dispose();
        _deviceApplyVersion++;
        _pendingTrayDeviceSteps = 0;
        _pendingTrayVolumeSteps = 0;
        _volumeApplyVersions.Clear();
    }
}
