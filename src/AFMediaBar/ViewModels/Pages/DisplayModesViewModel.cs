using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

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
    private readonly LocalizationService _localization;
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
    /// 模式名取自与模式卡片相同的键，因此芯片与卡片任何时候都写作同一个词。
    /// Text for the header status chip. It reads the real <see cref="WindowMode"/> rather than this page's
    /// preview selection, because the in-page selection only changes the highlight and never switches the
    /// window; the two must not be conflated. The mode name comes from the same keys the mode cards use, so the
    /// chip and the cards always read as one word.
    /// </summary>
    public string HostingModeText => Translations.Format(
        "DisplayModes.Status.Current",
        Translations.Get(CurrentWindowMode == WindowMode.Taskbar ? "DisplayModes.Mode.Taskbar" : "Common.DynamicIsland"));

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

    /// <summary>固定宽度的可用范围提示，随任务栏长度约束变化；单位与默认值一样放在括号与数字里。/ Tooltip for the fixed width's available range, which follows the taskbar length constraints; the unit sits with the numbers as it does for every default value.</summary>
    public string FixedTaskbarLengthRangeText =>
        Translations.Format("DisplayModes.Width.RangeText", FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum);

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

    /// <summary>
    /// 完整层当前生效的分区组合，作为状态文本显示。三个名称与「套用预设」按钮上的文案取自同一批键。
    /// The section combination the full panel currently uses, shown as a status text. The three names come from the
    /// same keys as the captions on the "apply a preset" buttons.
    /// </summary>
    public string FullPanelLayoutStatus => FullPanelSettings == TaskbarFullPanelSettings.Compact
        ? Translations.Get("DisplayModes.Full.Preset.Compact")
        : FullPanelSettings == TaskbarFullPanelSettings.Full
            ? Translations.Get("DisplayModes.Full.Preset.Full")
            : Translations.Get("DisplayModes.Full.Preset.Custom");

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

    /// <summary>
    /// 创建显示模式页的视图模型并订阅设置、显示器与长度约束三处变化。
    /// 三个订阅源都是单例，因此订阅与进程同寿命，不需要退订。
    /// Creates the display-mode view model and subscribes to settings, monitor, and length-constraint changes.
    /// All three sources are singletons, so the subscriptions live as long as the process and never need cancelling.
    /// </summary>
    /// <param name="displayMonitorService">显示器目录，供目标显示器下拉框使用。/ Display catalog behind the target-monitor drop-down.</param>
    /// <param name="taskbarLengthConstraints">任务栏可用长度约束，决定固定宽度的取值范围。/ Taskbar length constraints that bound the fixed width.</param>
    /// <param name="localization">
    /// 界面语言。本页有三处文案由代码拼出（页头状态芯片、固定宽度可用范围、显示器名称），语言变化时必须重新取值。
    /// 它是必填依赖：可省略的依赖会留下一条"忘记注入就静默不刷新"的路径，而这里没有合理的缺省语言。
    /// Interface language. Three strings on this page are composed in code (the header status chip, the fixed-width
    /// available range, and the monitor names) and have to be re-read when the language changes. It is a required dependency:
    /// an omittable one leaves a path where forgetting to inject it silently skips the refresh, and there is no sensible
    /// default language here.
    /// </param>
    public DisplayModesViewModel(
        IDisplayMonitorService displayMonitorService,
        TaskbarLengthConstraintsService taskbarLengthConstraints,
        LocalizationService localization)
    {
        _displayMonitorService = displayMonitorService;
        _taskbarLengthConstraints = taskbarLengthConstraints;
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;
        _taskbarLengthConstraints.Changed += OnTaskbarLengthConstraintsChanged;

        // 代码拼出来的文案不会随 XAML 的动态资源一起更新，因此在语言变化时重新通知一遍。
        // Text composed in code does not follow the XAML dynamic resources, so it is announced again when the language
        // changes.
        _localization.LanguageChanged += OnLanguageChanged;

        _displayMonitorService.Refresh();
        RefreshMonitorOptions();
    }

    /// <summary>
    /// 语言变化后重新取值：显示器名称是构建下拉框列表时拼出来的字符串，不会随绑定自己更新，因此列表重建一次；
    /// 空的属性名让 WPF 重读其余全部绑定，避免逐个列出属性名时漏掉一个而留下半页旧语言。
    /// Re-reads the composed strings after a language change: monitor names are built while the drop-down list is
    /// created and do not follow a binding on their own, so that list is rebuilt, and the empty property name makes WPF
    /// re-read every other binding instead of listing properties one by one and missing one.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshMonitorOptions();
        OnPropertyChanged(string.Empty);
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
        // 显示器名称在这里拼装成列表项，因此「主显示器」后缀由文案提供：它与媒体与通知页的显示器列表共用同一个键，
        // 两处必须逐字一致；中文用全角括号、英文需要一个空格，这个空格归文案所有，代码不猜语言。
        // Monitor names are composed into the list items here, so the "primary" suffix comes from the text registry under
        // the same key as the media-and-notifications monitor list, and the two have to match word for word. Chinese uses
        // full-width brackets while English needs a space, so that space belongs to the text rather than to code that would
        // have to guess the language.
        var primarySuffix = Translations.Get("Common.Monitor.PrimarySuffix");
        var disconnectedSuffix = Translations.Get("DisplayModes.Monitor.DisconnectedSuffix");
        var monitors = _displayMonitorService.GetMonitors();
        var options = monitors
            .Select((monitor, index) => new DisplayMonitorOption(
                monitor.DeviceId,
                $"{index + 1}. {monitor.DeviceName}{(monitor.IsPrimary ? primarySuffix : string.Empty)}",
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
                    $"{preferredId}{disconnectedSuffix}",
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
