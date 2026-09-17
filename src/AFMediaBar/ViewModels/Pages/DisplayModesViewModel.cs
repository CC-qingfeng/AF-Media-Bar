using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>设置页中可预览选择的显示模式。 / Display mode selectable for preview on the settings page.</summary>
public enum DisplayModeSelection
{
    Taskbar = 0,
    DynamicIsland = 1,
    DesktopCard = 2,
    FloatingBall = 3
}

/// <summary>四种显示模式及任务栏轻度自定义。 / Four display modes and light taskbar customization.</summary>
public partial class DisplayModesViewModel : ObservableObject
{
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
    private bool _isRefreshing;
    private DisplayModeSelection _selectedMode = DisplayModeSelection.Taskbar;
    private IReadOnlyList<DisplayMonitorOption> _monitorOptions = Array.Empty<DisplayMonitorOption>();

    public IReadOnlyList<DisplayMonitorOption> MonitorOptions => _monitorOptions;
    public WindowMode CurrentWindowMode => SettingsManager.Current.WindowMode;
    public DisplayModeSelection SelectedMode => _selectedMode;
    public bool IsTaskbarMode => SelectedMode == DisplayModeSelection.Taskbar;
    public bool IsDynamicIslandMode => SelectedMode == DisplayModeSelection.DynamicIsland;
    public bool IsDesktopCardMode => SelectedMode == DisplayModeSelection.DesktopCard;
    public bool IsFloatingBallMode => SelectedMode == DisplayModeSelection.FloatingBall;
    public bool IsUnimplementedMode => !IsTaskbarMode;

    /// <summary>
    /// 当前显示模式的文本，供页头状态芯片显示。它读的是真实 <see cref="WindowMode"/>，
    /// 而不是本页的预览选择——页内选择只改变高亮，从不切换窗口，两者不能混为一谈。
    /// Text for the header status chip. It reads the real <see cref="WindowMode"/> rather than this page's
    /// preview selection, because the in-page selection only changes the highlight and never switches the
    /// window; the two must not be conflated.
    /// </summary>
    public string HostingModeText => CurrentWindowMode == WindowMode.Taskbar ? "当前：任务栏" : "当前：灵动岛";

    /// <summary>
    /// 任务栏是否就是当前运行模式。模式卡片用它决定“当前模式”芯片是否显示，
    /// 取代以前写死在任务栏卡片上的那个芯片。
    /// Whether the taskbar really is the running mode. The mode cards use it to decide whether the
    /// "current mode" chip shows, replacing the chip that used to be hardcoded on the taskbar card.
    /// </summary>
    public bool IsTaskbarHostingActive => CurrentWindowMode == WindowMode.Taskbar;

    /// <summary>灵动岛背景方案。/ Dynamic-island background scheme.</summary>
    public DynamicIslandBackgroundMode DynamicIslandBackgroundMode
    {
        get => SettingsManager.Current.DynamicIslandBackgroundMode;
        set
        {
            if (_isRefreshing || SettingsManager.Current.DynamicIslandBackgroundMode == value) return;
            SettingsManager.Current.DynamicIslandBackgroundMode = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    /// <summary>灵动岛贴靠边缘。/ Edge where the dynamic island docks.</summary>
    public DynamicIslandEdge DynamicIslandEdge
    {
        get => SettingsManager.Current.DynamicIslandEdge;
        set
        {
            if (_isRefreshing || SettingsManager.Current.DynamicIslandEdge == value) return;
            SettingsManager.Current.DynamicIslandEdge = value;
            SettingsManager.Current.DynamicIslandEdgeDocked = true;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public bool TrackChangeNotificationEnabled
    {
        get => NotificationSettings.Enabled;
        set => UpdateNotification(NotificationSettings with { Enabled = value });
    }

    public bool ShowTrackChangeNotificationWhenFullscreen
    {
        get => NotificationSettings.ShowWhenFullscreen;
        set => UpdateNotification(NotificationSettings with { ShowWhenFullscreen = value });
    }

    public int TrackChangeNotificationDurationSeconds
    {
        get => NotificationSettings.DurationMilliseconds / 1000;
        set => UpdateNotification(NotificationSettings with { DurationMilliseconds = value * 1000 });
    }

    public TrackChangeNotificationPosition TrackChangeNotificationPosition
    {
        get => NotificationSettings.Position;
        set => UpdateNotification(NotificationSettings with { Position = value });
    }

    public NotificationTargetMode TrackChangeNotificationTargetMode
    {
        get => NotificationSettings.TargetMode;
        set => UpdateNotification(NotificationSettings with { TargetMode = value });
    }

    public string? TrackChangeNotificationFixedMonitorDeviceId
    {
        get => NotificationSettings.FixedMonitorDeviceId ?? _displayMonitorService.ResolveFixedMonitor(null)?.DeviceId;
        set => UpdateNotification(NotificationSettings with { FixedMonitorDeviceId = value });
    }

    public bool CanSelectTrackChangeNotificationMonitor =>
        TrackChangeNotificationEnabled && TrackChangeNotificationTargetMode == NotificationTargetMode.Fixed;

    public string? TaskbarTargetMonitorDeviceId
    {
        get => SettingsManager.Current.TaskbarTargetMonitorDeviceId ?? _displayMonitorService.ResolveFixedMonitor(null)?.DeviceId;
        set
        {
            if (_isRefreshing || string.Equals(
                    SettingsManager.Current.TaskbarTargetMonitorDeviceId,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SettingsManager.Current.TaskbarTargetMonitorDeviceId = value;
            OnPropertyChanged();
        }
    }

    public bool HoverLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.HoverLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { HoverLayerEnabled = value });
    }

    public bool FullLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.FullLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullLayerEnabled = value });
    }

    public TaskbarInformationDensity Density
    {
        get => SettingsManager.Current.TaskbarExperience.Density;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { Density = value });
    }

    public TaskbarContentLayout ContentLayout
    {
        get => SettingsManager.Current.TaskbarExperience.ContentLayout;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { ContentLayout = value });
    }

    /// <summary>任务栏标题和歌手文字的对齐方式。 / Alignment of taskbar title and artist text.</summary>
    public TaskbarMediaTextAlignment MediaTextAlignment
    {
        get => SettingsManager.Current.TaskbarExperience.MediaTextAlignment;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { MediaTextAlignment = value });
    }

    /// <summary>
    /// 静置层与悬停层是否提供进入完整层的入口（文字区顶部细杠与悬停层按钮）。界面开关在本页静置层分区，
    /// 关闭后两个入口一起消失；完整层本身与托盘、菜单入口不受影响。
    /// Whether the rest and hover layers offer an entry into the full layer (the thin bar above the text and the hover layer's
    /// button). The switch sits in this page's rest section; turning it off removes both entries, while the full layer itself and
    /// the tray and menu entries stay as they are.
    /// </summary>
    public bool FullPanelEntryVisible
    {
        get => SettingsManager.Current.TaskbarExperience.FullPanelEntryVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullPanelEntryVisible = value });
    }

    /// <summary>静置层是否显示底部的播放进度条。 / Whether the rest layer shows its bottom playback-progress bar.</summary>
    public bool RestProgressVisible
    {
        get => SettingsManager.Current.TaskbarExperience.RestProgressVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { RestProgressVisible = value });
    }

    /// <summary>静置层是否显示频谱组件。 / Whether the rest layer shows the spectrum component.</summary>
    public bool SpectrumVisible
    {
        get => SettingsManager.Current.TaskbarExperience.SpectrumVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { SpectrumVisible = value });
    }

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible
    {
        get => SettingsManager.Current.TaskbarExperience.PerformanceVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { PerformanceVisible = value });
    }

    public bool HoverPlayPauseVisible
    {
        get => HoverControls.PlayPauseVisible;
        set => UpdateHoverControls(HoverControls with { PlayPauseVisible = value });
    }

    public bool HoverPreviousNextVisible
    {
        get => HoverControls.PreviousNextVisible;
        set => UpdateHoverControls(HoverControls with { PreviousNextVisible = value });
    }

    public bool HoverOutputDeviceVisible
    {
        get => HoverControls.OutputDeviceVisible;
        set => UpdateHoverControls(HoverControls with { OutputDeviceVisible = value });
    }

    public bool HoverAudioControlVisible
    {
        get => HoverControls.AudioControlVisible;
        set => UpdateHoverControls(HoverControls with { AudioControlVisible = value });
    }

    public bool HoverProgressVisible
    {
        get => HoverControls.ProgressVisible;
        set => UpdateHoverControls(HoverControls with { ProgressVisible = value });
    }

    private TaskbarHoverControlsSettings HoverControls =>
        SettingsManager.Current.TaskbarExperience.HoverControls;

    /// <summary>当前模式的组件间距（DIP）。/ Component gap for the current mode in DIP.</summary>
    public double ComponentSpacingDip
    {
        get => SettingsManager.Current.TaskbarExperience.ComponentSpacingDip;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { ComponentSpacingDip = value });
    }

    public bool FollowMediaTextLength
    {
        get => SettingsManager.Current.TaskbarExperience.LengthMode == TaskbarLengthMode.FollowContent;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            LengthMode = value ? TaskbarLengthMode.FollowContent : TaskbarLengthMode.Fixed,
            FixedLengthDip = value
                ? SettingsManager.Current.TaskbarExperience.FixedLengthDip
                : Math.Clamp(
                    SettingsManager.Current.TaskbarExperience.FixedLengthDip,
                    FixedTaskbarLengthMinimum,
                    FixedTaskbarLengthMaximum)
        });
    }

    public bool UsesFixedTaskbarLength => !FollowMediaTextLength;
    public double FixedTaskbarLengthMinimum => Math.Ceiling(_taskbarLengthConstraints.MinimumLengthDip);
    public double FixedTaskbarLengthMaximum => Math.Max(FixedTaskbarLengthMinimum, Math.Floor(_taskbarLengthConstraints.MaximumLengthDip));

    public double FixedTaskbarLengthDip
    {
        get => Math.Clamp(
            SettingsManager.Current.TaskbarExperience.FixedLengthDip,
            FixedTaskbarLengthMinimum,
            FixedTaskbarLengthMaximum);
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            FixedLengthDip = Math.Clamp(value, FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
        });
    }

    public string FixedTaskbarLengthRangeText =>
        $"可用范围：{FixedTaskbarLengthMinimum:0}–{FixedTaskbarLengthMaximum:0} DIP";

    public bool FullPanelMediaInfoVisible
    {
        get => FullPanelSettings.MediaInfoVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.MediaInfo, value);
    }

    public bool FullPanelMediaControlsVisible
    {
        get => FullPanelSettings.MediaControlsVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.MediaControls, value);
    }

    public bool FullPanelAudioControlsVisible
    {
        get => FullPanelSettings.AudioControlsVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.AudioControls, value);
    }

    public bool FullPanelPerformanceVisible
    {
        get => FullPanelSettings.PerformanceVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.Performance, value);
    }

    public bool CanToggleFullPanelMediaInfo => CanToggleFullPanelGroup(FullPanelSettings.MediaInfoVisible);
    public bool CanToggleFullPanelMediaControls => CanToggleFullPanelGroup(FullPanelSettings.MediaControlsVisible);
    public bool CanToggleFullPanelAudioControls => CanToggleFullPanelGroup(FullPanelSettings.AudioControlsVisible);
    public bool CanToggleFullPanelPerformance => CanToggleFullPanelGroup(FullPanelSettings.PerformanceVisible);

    public string FullPanelLayoutStatus => FullPanelSettings == TaskbarFullPanelSettings.Compact
        ? "紧凑"
        : FullPanelSettings == TaskbarFullPanelSettings.Full ? "完整" : "自定义";

    private TaskbarFullPanelSettings FullPanelSettings => SettingsManager.Current.TaskbarExperience.FullPanel.Normalize();

    /// <summary>
    /// 灵动岛模式自己的背景样式。它写在 <c>DynamicIslandSurface</c> 上，与任务栏表面互相独立。
    /// 界面位于显示模式页的灵动岛分区，因此属性也归这里，避免同一份设置被两个视图模型各写一次。
    /// The dynamic island's own background style, stored on <c>DynamicIslandSurface</c> and independent of the
    /// taskbar surface. The interface lives in the display-mode page's island section, so the property lives here
    /// too and one setting is never written from two view models.
    /// </summary>
    public PlayerSurfaceStyle IslandSurfaceStyle
    {
        get => IslandSurface.Style;
        set => PublishIslandSurface(IslandSurface with { Style = value });
    }

    /// <inheritdoc cref="IslandSurfaceStyle" />
    public int IslandSurfaceOpacityPercent
    {
        get => IslandSurface.BackgroundOpacityPercent;
        set => PublishIslandSurface(IslandSurface with { BackgroundOpacityPercent = value });
    }

    /// <inheritdoc cref="IslandSurfaceStyle" />
    public double IslandSurfaceCornerRadiusDip
    {
        get => IslandSurface.CornerRadiusDip;
        set => PublishIslandSurface(IslandSurface with { CornerRadiusDip = value });
    }

    private static ModeSurfaceSettings IslandSurface => SettingsManager.Current.DynamicIslandSurface;

    private void PublishIslandSurface(ModeSurfaceSettings settings)
    {
        SettingsManager.Current.DynamicIslandSurface = settings.Normalize();
        OnPropertyChanged(nameof(IslandSurfaceStyle));
        OnPropertyChanged(nameof(IslandSurfaceOpacityPercent));
        OnPropertyChanged(nameof(IslandSurfaceCornerRadiusDip));
    }

    public LayoutOrientationMode Orientation
    {
        get => SettingsManager.Current.LayoutOrientationMode;
        set
        {
            if (_isRefreshing || SettingsManager.Current.LayoutOrientationMode == value) return;
            SettingsManager.Current.LayoutOrientationMode = value;
            SettingsManager.RaiseLayoutSettingsChanged(SettingsManager.Current.WindowMode, value);
            OnPropertyChanged();
        }
    }

    public bool IsTaskbarPositionLocked
    {
        get => SettingsManager.Current.TaskbarBarPositionLocked;
        set { SettingsManager.Current.TaskbarBarPositionLocked = value; OnPropertyChanged(); }
    }

    public bool IsTaskbarAvoidingIcons
    {
        get => SettingsManager.Current.TaskbarBarAvoidIcons;
        set
        {
            SettingsManager.Current.TaskbarBarAvoidIcons = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public double TaskbarCrossAxisOffsetDip
    {
        get => SettingsManager.Current.TaskbarBarCrossAxisOffsetDip;
        set
        {
            SettingsManager.Current.TaskbarBarCrossAxisOffsetDip = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public DisplayModesViewModel(
        IDisplayMonitorService displayMonitorService,
        TaskbarLengthConstraintsService taskbarLengthConstraints)
    {
        _displayMonitorService = displayMonitorService;
        _taskbarLengthConstraints = taskbarLengthConstraints;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;
        _taskbarLengthConstraints.Changed += OnTaskbarLengthConstraintsChanged;
        _displayMonitorService.Refresh();
        RefreshMonitorOptions();
    }

    [RelayCommand] private void SwitchToTaskbarMode() => SelectMode(DisplayModeSelection.Taskbar);
    [RelayCommand] private void SwitchToDynamicIslandMode() => SelectMode(DisplayModeSelection.DynamicIsland);
    [RelayCommand] private void SwitchToDesktopCardMode() => SelectMode(DisplayModeSelection.DesktopCard);
    [RelayCommand] private void SwitchToFloatingBallMode() => SelectMode(DisplayModeSelection.FloatingBall);
    [RelayCommand] private void ApplyCompactFullPanelPreset() => UpdateFullPanel(TaskbarFullPanelSettings.Compact);
    [RelayCommand] private void ApplyFullFullPanelPreset() => UpdateFullPanel(TaskbarFullPanelSettings.Full);

    [RelayCommand]
    private void ResetTaskbarPosition()
    {
        SettingsManager.Current.Position = TaskbarBarPosition.Start;
        SettingsManager.Current.TaskbarBarManualPadding = 0;
        TaskbarCrossAxisOffsetDip = 0;
        SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
    }

    private void SelectMode(DisplayModeSelection mode)
    {
        if (_selectedMode == mode) return;
        _selectedMode = mode;
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsTaskbarMode));
        OnPropertyChanged(nameof(IsDynamicIslandMode));
        OnPropertyChanged(nameof(IsDesktopCardMode));
        OnPropertyChanged(nameof(IsFloatingBallMode));
        OnPropertyChanged(nameof(IsUnimplementedMode));
    }

    private void UpdateExperience(TaskbarExperienceSettings value)
    {
        // 页面上高亮一个未实现的承载模式时，任务栏专属设置不接受写入——这是既有且受测试保护的不变量。
        // 代价是那些控件会“看着能改、实际不保存”，因此页面在同一状态下会显示一条明确的只读提示，
        // 而不是让用户自己猜。提示由 DisplayModesPage 绑定 IsUnimplementedMode 呈现。
        // While an unimplemented hosting mode is highlighted, taskbar-only settings refuse writes: that is an
        // existing invariant guarded by a test. The cost is controls that look editable without saving, so the
        // page shows an explicit read-only notice in that state instead of leaving the user to guess.
        if (_isRefreshing || !IsTaskbarMode) return;
        SettingsManager.SetTaskbarExperienceSettings(value.Normalize());
        RaiseExperience();
    }

    private void UpdateHoverControls(TaskbarHoverControlsSettings controls) =>
        UpdateExperience(SettingsManager.Current.TaskbarExperience with { HoverControls = controls });

    private void UpdateNotification(TrackChangeNotificationSettings value)
    {
        if (_isRefreshing)
            return;

        SettingsManager.SetTrackChangeNotificationSettings(value.Normalize());
        RaiseNotification();
    }

    private void UpdateFullPanelGroup(FullPanelGroup group, bool visible)
    {
        if (_isRefreshing) return;
        var current = FullPanelSettings;
        var updated = group switch
        {
            FullPanelGroup.MediaInfo => current with { MediaInfoVisible = visible },
            FullPanelGroup.MediaControls => current with { MediaControlsVisible = visible },
            FullPanelGroup.AudioControls => current with { AudioControlsVisible = visible },
            FullPanelGroup.Performance => current with { PerformanceVisible = visible },
            _ => current
        };
        if (!updated.MediaInfoVisible && !updated.MediaControlsVisible &&
            !updated.AudioControlsVisible && !updated.PerformanceVisible)
        {
            RaiseFullPanel();
            return;
        }
        UpdateFullPanel(updated);
    }

    private void UpdateFullPanel(TaskbarFullPanelSettings settings) =>
        UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullPanel = settings.Normalize() });

    private bool CanToggleFullPanelGroup(bool visible) => !visible || VisibleFullPanelGroupCount() > 1;

    private int VisibleFullPanelGroupCount()
    {
        var settings = FullPanelSettings;
        return (settings.MediaInfoVisible ? 1 : 0) +
               (settings.MediaControlsVisible ? 1 : 0) +
               (settings.AudioControlsVisible ? 1 : 0) +
               (settings.PerformanceVisible ? 1 : 0);
    }

    public void ResetDisplayModes() => SettingsManager.ResetDisplayModes();
    public void ResetExtraFeatures() => SettingsManager.ResetExtraFeatures();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.DisplayModes or SettingsResetScope.Layout or SettingsResetScope.All)
            RaiseAll();
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => RefreshMonitorOptions();

    private void OnTaskbarLengthConstraintsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
        OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip));
        OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
    }

    private void RefreshMonitorOptions()
    {
        var monitors = _displayMonitorService.GetMonitors();
        var options = monitors
            .Select((monitor, index) => new DisplayMonitorOption(
                monitor.DeviceId,
                $"{index + 1}. {monitor.DeviceName}{(monitor.IsPrimary ? "（主显示器）" : string.Empty)}",
                monitor.IsPrimary))
            .ToList();

        var preferredIds = new[]
        {
            SettingsManager.Current.TaskbarTargetMonitorDeviceId,
            NotificationSettings.FixedMonitorDeviceId
        };
        foreach (var preferredId in preferredIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (options.All(option => !string.Equals(option.DeviceId, preferredId, StringComparison.OrdinalIgnoreCase)))
            {
                options.Insert(0, new DisplayMonitorOption(
                    preferredId!,
                    $"{preferredId}（当前未连接）",
                    false,
                    false));
            }
        }

        _monitorOptions = options;
        OnPropertyChanged(nameof(MonitorOptions));
        OnPropertyChanged(nameof(TaskbarTargetMonitorDeviceId));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
    }

    private void RaiseAll()
    {
        _isRefreshing = true;
        try
        {
            OnPropertyChanged(nameof(CurrentWindowMode)); OnPropertyChanged(nameof(IsTaskbarMode)); OnPropertyChanged(nameof(IsDynamicIslandMode));
            OnPropertyChanged(nameof(IsDesktopCardMode)); OnPropertyChanged(nameof(IsFloatingBallMode)); OnPropertyChanged(nameof(IsUnimplementedMode));
            OnPropertyChanged(nameof(HostingModeText)); OnPropertyChanged(nameof(IsTaskbarHostingActive));
            OnPropertyChanged(nameof(DynamicIslandBackgroundMode)); OnPropertyChanged(nameof(DynamicIslandEdge));
            OnPropertyChanged(nameof(IslandSurfaceStyle)); OnPropertyChanged(nameof(IslandSurfaceOpacityPercent));
            OnPropertyChanged(nameof(IslandSurfaceCornerRadiusDip));
            RaiseExperience(); OnPropertyChanged(nameof(Orientation)); OnPropertyChanged(nameof(IsTaskbarPositionLocked));
            OnPropertyChanged(nameof(IsTaskbarAvoidingIcons)); OnPropertyChanged(nameof(TaskbarCrossAxisOffsetDip));
            OnPropertyChanged(nameof(TaskbarTargetMonitorDeviceId));
            RaiseNotification();
        }
        finally { _isRefreshing = false; }
    }

    private void RaiseExperience()
    {
        OnPropertyChanged(nameof(HoverLayerEnabled)); OnPropertyChanged(nameof(FullLayerEnabled));
        OnPropertyChanged(nameof(Density)); OnPropertyChanged(nameof(ContentLayout));
        OnPropertyChanged(nameof(MediaTextAlignment));
        OnPropertyChanged(nameof(FullPanelEntryVisible));
        OnPropertyChanged(nameof(RestProgressVisible));
        OnPropertyChanged(nameof(SpectrumVisible)); OnPropertyChanged(nameof(PerformanceVisible));
        OnPropertyChanged(nameof(HoverPlayPauseVisible)); OnPropertyChanged(nameof(HoverPreviousNextVisible));
        OnPropertyChanged(nameof(HoverOutputDeviceVisible)); OnPropertyChanged(nameof(HoverAudioControlVisible));
        OnPropertyChanged(nameof(HoverProgressVisible));
        OnPropertyChanged(nameof(ComponentSpacingDip));
        OnPropertyChanged(nameof(FollowMediaTextLength)); OnPropertyChanged(nameof(UsesFixedTaskbarLength));
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum)); OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip)); OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
        RaiseFullPanel();
    }

    private void RaiseNotification()
    {
        OnPropertyChanged(nameof(TrackChangeNotificationEnabled));
        OnPropertyChanged(nameof(ShowTrackChangeNotificationWhenFullscreen));
        OnPropertyChanged(nameof(TrackChangeNotificationDurationSeconds));
        OnPropertyChanged(nameof(TrackChangeNotificationPosition));
        OnPropertyChanged(nameof(TrackChangeNotificationTargetMode));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
        OnPropertyChanged(nameof(CanSelectTrackChangeNotificationMonitor));
    }

    private TrackChangeNotificationSettings NotificationSettings =>
        SettingsManager.Current.TrackChangeNotification.Normalize();

    private void RaiseFullPanel()
    {
        OnPropertyChanged(nameof(FullPanelMediaInfoVisible));
        OnPropertyChanged(nameof(FullPanelMediaControlsVisible));
        OnPropertyChanged(nameof(FullPanelAudioControlsVisible));
        OnPropertyChanged(nameof(FullPanelPerformanceVisible));
        OnPropertyChanged(nameof(CanToggleFullPanelMediaInfo));
        OnPropertyChanged(nameof(CanToggleFullPanelMediaControls));
        OnPropertyChanged(nameof(CanToggleFullPanelAudioControls));
        OnPropertyChanged(nameof(CanToggleFullPanelPerformance));
        OnPropertyChanged(nameof(FullPanelLayoutStatus));
    }

    private enum FullPanelGroup
    {
        MediaInfo,
        MediaControls,
        AudioControls,
        Performance
    }
}
