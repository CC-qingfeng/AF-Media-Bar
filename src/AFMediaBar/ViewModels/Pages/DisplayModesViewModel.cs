using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>四种显示模式及任务栏轻度自定义。 / Four display modes and light taskbar customization.</summary>
public partial class DisplayModesViewModel : ObservableObject
{
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
    private bool _isRefreshing;
    private IReadOnlyList<DisplayMonitorOption> _monitorOptions = Array.Empty<DisplayMonitorOption>();

    public IReadOnlyList<DisplayMonitorOption> MonitorOptions => _monitorOptions;
    public WindowMode CurrentWindowMode => SettingsManager.Current.WindowMode;
    public bool IsTaskbarMode => CurrentWindowMode == WindowMode.Taskbar;
    public bool IsDynamicIslandMode => CurrentWindowMode == WindowMode.DynamicIsland;

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

    [RelayCommand] private void SwitchToTaskbarMode() => SwitchMode(WindowMode.Taskbar);
    [RelayCommand] private void SwitchToDynamicIslandMode() => SwitchMode(WindowMode.DynamicIsland);
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

    private void SwitchMode(WindowMode mode)
    {
        if (CurrentWindowMode == mode) return;
        SettingsManager.Current.WindowMode = mode;
        SettingsManager.RaiseLayoutSettingsChanged(mode, Orientation);
        RaiseAll();
    }

    private void UpdateExperience(TaskbarExperienceSettings value)
    {
        if (_isRefreshing) return;
        SettingsManager.SetTaskbarExperienceSettings(value.Normalize());
        RaiseExperience();
    }

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
            OnPropertyChanged(nameof(DynamicIslandBackgroundMode)); OnPropertyChanged(nameof(DynamicIslandEdge));
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
