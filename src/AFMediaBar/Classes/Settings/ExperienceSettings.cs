using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;

/// <summary>旧播放器交互预设，仅保留用于读取早期设置。 / Legacy player-interaction preset retained only for reading older settings.</summary>
public enum MediaInteractionMode
{
    Buttons = 0,
    Hybrid = 1,
    Gestures = 2
}

/// <summary>播放器表面滚轮可绑定的媒体或音频动作。 / Media or audio action bindable to player-surface wheel input.</summary>
public enum WheelAction
{
    PreviousNext = 0,
    CurrentApplicationVolume = 1,
    OutputDevice = 2,
    SwitchMediaSource = 3
}

/// <summary>旧组合滚轮鼠标按键，仅保留用于读取早期设置。 / Legacy mouse chord retained only for reading older settings.</summary>
public enum MouseChordButton
{
    Left = 0,
    Right = 1
}

/// <summary>单击通知区域图标时执行的动作。 / Action performed when the notification-area icon is clicked.</summary>
public enum TrayClickAction
{
    None = 0,
    OpenSettings = 1,
    OpenAudioControl = 2,
    OpenContextMenu = 3
}

/// <summary>任务栏静置内容的点击结果。 / Result of clicking taskbar rest-layer content.</summary>
public enum PlayerClickAction
{
    TogglePlayPause = 0,
    ActivateSource = 1
}

/// <summary>普通滚轮映射切换到组合映射时使用的共享修饰键。 / Shared modifier that switches plain wheel input to its chord mapping.</summary>
public enum InteractionModifier
{
    Shift = 0,
    LeftMouseButton = 1,
    RightMouseButton = 2
}

/// <summary>任务栏固定布局的信息密度。 / Information density for the fixed taskbar layout.</summary>
public enum TaskbarInformationDensity
{
    Minimal = 0,
    Balanced = 1,
    Information = 2
}

/// <summary>任务栏媒体文字的固定布局。 / Fixed layout used by taskbar media text.</summary>
public enum TaskbarContentLayout
{
    CompactInline = 0,
    AdaptiveStack = 1,
    CenteredStack = 2
}

/// <summary>任务栏标题和歌手文字的对齐方式。 / Alignment of taskbar title and artist text.</summary>
public enum TaskbarMediaTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>任务栏媒体条主轴长度的决定方式。 / How the taskbar media bar resolves its primary-axis length.</summary>
public enum TaskbarLengthMode
{
    FollowContent = 0,
    Fixed = 1
}

/// <summary>播放器表面的基础背景方案。 / Basic background style for a player surface.</summary>
public enum PlayerSurfaceStyle
{
    Automatic = 0,
    Solid = 1,
    ThemeTint = 2
}

/// <summary>歌词文字对齐方式。 / Lyric text alignment.</summary>
public enum LyricsTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>SMTC 应用来源允许列表。 / Allow-list for SMTC application sources.</summary>
public readonly record struct SmtcSourceFilterSettings(bool Enabled, IReadOnlyList<string>? AllowedSourceIds)
{
    public static SmtcSourceFilterSettings Default { get; } = new(false, []);

    public SmtcSourceFilterSettings Normalize() => this with
    {
        AllowedSourceIds = (AllowedSourceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };
}

/// <summary>快速启动列表设置。 / Quick-launch list settings.</summary>
public readonly record struct QuickLaunchSettings(IReadOnlyList<QuickLaunchEntry>? Entries)
{
    public static QuickLaunchSettings Default { get; } = new([]);

    public QuickLaunchSettings Normalize()
    {
        var entries = new List<QuickLaunchEntry>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Target) || !Enum.IsDefined(entry.Kind))
                continue;
            var target = entry.Target.Trim();
            if (!targets.Add($"{entry.Kind}:{target}"))
                continue;
            var id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? Path.GetFileNameWithoutExtension(target)
                : entry.DisplayName.Trim();
            entries.Add(entry with
            {
                Id = id,
                DisplayName = displayName,
                Target = target,
                SourceId = string.IsNullOrWhiteSpace(entry.SourceId) ? null : entry.SourceId.Trim()
            });
        }
        return new QuickLaunchSettings(entries);
    }
}

/// <summary>任务栏频谱组件设置。 / Taskbar spectrum component settings.</summary>
public readonly record struct SpectrumComponentSettings(int BandCount, int RefreshRateHz, int SensitivityPercent)
{
    public static SpectrumComponentSettings Default { get; } = new(9, 20, 100);
    public SpectrumComponentSettings Normalize() => new(
        Math.Clamp(BandCount, 1, 9),
        Math.Clamp(RefreshRateHz, 5, 30),
        Math.Clamp(SensitivityPercent, 1, 400));
}

/// <summary>任务栏性能组件设置。 / Taskbar performance component settings.</summary>
public readonly record struct PerformanceComponentSettings(
    IReadOnlyList<MetricKind>? Metrics,
    int RefreshIntervalMilliseconds,
    bool OpenTaskManagerOnClick)
{
    public static PerformanceComponentSettings Default { get; } = new([MetricKind.SystemMemory], 2500, false);

    public PerformanceComponentSettings Normalize()
    {
        var metrics = (Metrics ?? [])
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(metric => metric)
            .ToArray();
        if (metrics.Length == 0)
            metrics = [MetricKind.SystemMemory];
        return new PerformanceComponentSettings(
            metrics,
            Math.Clamp(RefreshIntervalMilliseconds, 250, 60000),
            OpenTaskManagerOnClick);
    }
}

/// <summary>曲目切换通知在目标工作区中的位置。 / Position of the track-change notification in the target work area.</summary>
public enum TrackChangeNotificationPosition
{
    BottomLeft = 0,
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    BottomCenter = 4,
    BottomRight = 5
}

/// <summary>曲目切换通知选择显示器的方式。 / Method used to select the display for track-change notifications.</summary>
public enum NotificationTargetMode
{
    Fixed = 0,
    ForegroundWindow = 1
}

/// <summary>曲目切换通知的用户设置。 / User settings for track-change notifications.</summary>
public readonly record struct TrackChangeNotificationSettings(
    bool Enabled,
    bool ShowWhenFullscreen,
    int DurationMilliseconds,
    TrackChangeNotificationPosition Position,
    NotificationTargetMode TargetMode,
    string? FixedMonitorDeviceId)
{
    /// <summary>通知的默认设置。 / Default notification settings.</summary>
    public static TrackChangeNotificationSettings Default { get; } = new(
        false,
        false,
        1000,
        TrackChangeNotificationPosition.BottomLeft,
        NotificationTargetMode.Fixed,
        null);

    /// <summary>归一化枚举、时长和设备标识。 / Normalizes enums, duration, and the device identifier.</summary>
    public TrackChangeNotificationSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            DurationMilliseconds = Math.Clamp(DurationMilliseconds, 1000, 10000),
            Position = Enum.IsDefined(Position) ? Position : defaults.Position,
            TargetMode = Enum.IsDefined(TargetMode) ? TargetMode : defaults.TargetMode,
            FixedMonitorDeviceId = string.IsNullOrWhiteSpace(FixedMonitorDeviceId)
                ? null
                : FixedMonitorDeviceId.Trim()
        };
    }
}

/// <summary>任务栏完整层的功能组显隐设置。 / Visibility settings for taskbar full-panel feature groups.</summary>
public readonly record struct TaskbarFullPanelSettings(
    bool MediaInfoVisible,
    bool MediaControlsVisible,
    bool AudioControlsVisible,
    bool PerformanceVisible)
{
    /// <summary>仅显示媒体信息和媒体控制。 / Shows only media information and media controls.</summary>
    public static TaskbarFullPanelSettings Compact { get; } = new(true, true, false, false);

    /// <summary>显示所有功能组。 / Shows every feature group.</summary>
    public static TaskbarFullPanelSettings Full { get; } = new(true, true, true, true);

    /// <summary>完整层的默认设置。 / Default settings for the full panel.</summary>
    public static TaskbarFullPanelSettings Default => Full;

    /// <summary>确保至少保留一个功能组。 / Ensures at least one feature group remains visible.</summary>
    public TaskbarFullPanelSettings Normalize() =>
        MediaInfoVisible || MediaControlsVisible || AudioControlsVisible || PerformanceVisible
            ? this
            : Compact;
}

/// <summary>任务栏悬停层中各项控制的显隐设置。 / Visibility settings for controls in the taskbar hover layer.</summary>
public readonly record struct TaskbarHoverControlsSettings(
    bool PlayPauseVisible,
    bool PreviousNextVisible,
    bool OutputDeviceVisible,
    bool AudioControlVisible,
    bool ProgressVisible)
{
    /// <summary>手势优先的默认悬停控制组合。 / Default gesture-first hover-control combination.</summary>
    public static TaskbarHoverControlsSettings Default { get; } = new(false, false, true, true, true);
}

/// <summary>任务栏三层体验设置。 / Settings for the three-layer taskbar experience.</summary>
public readonly record struct TaskbarExperienceSettings(
    bool HoverLayerEnabled,
    bool FullLayerEnabled,
    TaskbarInformationDensity Density,
    TaskbarContentLayout ContentLayout,
    TaskbarFullPanelSettings FullPanel,
    TaskbarLengthMode LengthMode,
    double FixedLengthDip)
{
    /// <summary>任务栏组件之间的实际间距（DIP）。/ Actual gap between taskbar components in DIP.</summary>
    public double ComponentSpacingDip { get; init; } = 12;

    /// <summary>标题和歌手文字的对齐方式。 / Alignment of title and artist text.</summary>
    public TaskbarMediaTextAlignment MediaTextAlignment { get; init; } = TaskbarMediaTextAlignment.Left;

    /// <summary>静置层是否显示播放态频谱。 / Whether the rest layer shows the playing spectrum.</summary>
    public bool SpectrumVisible { get; init; } = true;

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible { get; init; } = true;

    /// <summary>悬停层中各项控制的显隐设置。 / Visibility settings for individual hover-layer controls.</summary>
    public TaskbarHoverControlsSettings HoverControls { get; init; } = TaskbarHoverControlsSettings.Default;

    /// <summary>
    /// 静置层媒体文字（标题、歌手、歌词）的字号缩放百分比。
    /// Font-size scale percentage for rest-layer media text: title, artist, and lyrics.
    /// </summary>
    public int MediaFontSizePercent { get; init; } = 100;

    /// <summary>组件间距的持久化安全下限。/ Persistence-safe lower bound for component spacing.</summary>
    public const double MinimumComponentSpacingDip = 4;

    /// <summary>组件间距的持久化安全上限。/ Persistence-safe upper bound for component spacing.</summary>
    public const double MaximumComponentSpacingDip = 32;

    /// <summary>静置层媒体文字字号缩放的持久化安全下限。/ Persistence-safe lower bound for the rest-layer media font-size scale.</summary>
    public const int MinimumMediaFontSizePercent = 80;

    /// <summary>
    /// 静置层媒体文字字号缩放的持久化安全上限。任务栏高度固定，标题与歌手两行必须容纳在该高度内，
    /// 因此上限保持在两行仍能完整显示的范围内。
    /// Persistence-safe upper bound for the rest-layer media font-size scale. The taskbar height is fixed and the title and
    /// artist must both fit inside it, so the upper bound keeps two lines fully visible.
    /// </summary>
    public const int MaximumMediaFontSizePercent = 125;

    /// <summary>固定长度设置的持久化安全下限。 / Persistence-safe lower bound for the fixed-length setting.</summary>
    public const double MinimumStoredFixedLengthDip = 120;

    /// <summary>固定长度设置的持久化安全上限；运行时仍按任务栏可用区间夹取。 / Persistence-safe upper bound; runtime still clamps to the taskbar's available range.</summary>
    public const double MaximumStoredFixedLengthDip = 4096;

    public static TaskbarExperienceSettings Default { get; } = new(
        true,
        true,
        TaskbarInformationDensity.Balanced,
        TaskbarContentLayout.AdaptiveStack,
        TaskbarFullPanelSettings.Default,
        TaskbarLengthMode.FollowContent,
        360);

    public TaskbarExperienceSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Density = Enum.IsDefined(Density) ? Density : defaults.Density,
            ContentLayout = Enum.IsDefined(ContentLayout) ? ContentLayout : defaults.ContentLayout,
            MediaTextAlignment = Enum.IsDefined(MediaTextAlignment) ? MediaTextAlignment : defaults.MediaTextAlignment,
            FullPanel = FullPanel.Normalize(),
            LengthMode = Enum.IsDefined(LengthMode) ? LengthMode : defaults.LengthMode,
            FixedLengthDip = double.IsFinite(FixedLengthDip) && FixedLengthDip >= MinimumStoredFixedLengthDip
                ? Math.Clamp(FixedLengthDip, MinimumStoredFixedLengthDip, MaximumStoredFixedLengthDip)
                : defaults.FixedLengthDip,
            ComponentSpacingDip = double.IsFinite(ComponentSpacingDip)
                ? Math.Clamp(ComponentSpacingDip, MinimumComponentSpacingDip, MaximumComponentSpacingDip)
                : defaults.ComponentSpacingDip,
            // schema 7 及更早的设置文件没有该字段，反序列化得到 0；0 与任何合法值都不同，因此回退到默认值。
            // Settings files up to schema 7 lack this field and deserialize it as 0; 0 is outside every legal value, so it
            // falls back to the default instead of being clamped to the minimum.
            MediaFontSizePercent = MediaFontSizePercent <= 0
                ? defaults.MediaFontSizePercent
                : Math.Clamp(MediaFontSizePercent, MinimumMediaFontSizePercent, MaximumMediaFontSizePercent)
        };
    }
}

/// <summary>任务栏播放器和托盘图标共用的点击与滚轮绑定。 / Click and wheel bindings shared by the taskbar player and tray icon.</summary>
public readonly record struct GlobalInteractionSettings(
    PlayerClickAction ArtworkClickAction,
    PlayerClickAction TextClickAction,
    WheelAction PrimaryWheelAction,
    InteractionModifier Modifier,
    WheelAction ChordWheelAction,
    TrayClickAction TrayClickAction,
    TrayWheelBehavior TrayPrimaryWheelAction,
    TrayWheelBehavior TrayChordWheelAction)
{
    public static GlobalInteractionSettings Default { get; } = new(
        PlayerClickAction.TogglePlayPause,
        PlayerClickAction.ActivateSource,
        WheelAction.PreviousNext,
        InteractionModifier.Shift,
        WheelAction.SwitchMediaSource,
        TrayClickAction.OpenAudioControl,
        TrayWheelBehavior.SwitchOutputDevice,
        TrayWheelBehavior.AdjustVolume);

    public GlobalInteractionSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            ArtworkClickAction = Enum.IsDefined(ArtworkClickAction) ? ArtworkClickAction : defaults.ArtworkClickAction,
            TextClickAction = Enum.IsDefined(TextClickAction) ? TextClickAction : defaults.TextClickAction,
            PrimaryWheelAction = Enum.IsDefined(PrimaryWheelAction) ? PrimaryWheelAction : defaults.PrimaryWheelAction,
            Modifier = Enum.IsDefined(Modifier) ? Modifier : defaults.Modifier,
            ChordWheelAction = Enum.IsDefined(ChordWheelAction) ? ChordWheelAction : defaults.ChordWheelAction,
            TrayClickAction = TrayClickAction is TrayClickAction.OpenAudioControl or TrayClickAction.OpenSettings or TrayClickAction.OpenContextMenu
                ? TrayClickAction
                : defaults.TrayClickAction,
            TrayPrimaryWheelAction = NormalizeTrayWheelAction(TrayPrimaryWheelAction, defaults.TrayPrimaryWheelAction),
            TrayChordWheelAction = NormalizeTrayWheelAction(TrayChordWheelAction, defaults.TrayChordWheelAction)
        };
    }

    private static TrayWheelBehavior NormalizeTrayWheelAction(TrayWheelBehavior action, TrayWheelBehavior fallback) =>
        action is TrayWheelBehavior.AdjustVolume or TrayWheelBehavior.SwitchOutputDevice ? action : fallback;
}

/// <summary>一个显示模式的基础表面外观。 / Basic surface appearance for one display mode.</summary>
public readonly record struct ModeSurfaceSettings(
    PlayerSurfaceStyle Style,
    int BackgroundOpacityPercent,
    double CornerRadiusDip)
{
    public static ModeSurfaceSettings Default { get; } = new(PlayerSurfaceStyle.Automatic, 100, 6);

    public ModeSurfaceSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Style = Enum.IsDefined(Style) ? Style : defaults.Style,
            BackgroundOpacityPercent = Math.Clamp(BackgroundOpacityPercent, 0, 100),
            CornerRadiusDip = double.IsFinite(CornerRadiusDip)
                ? Math.Clamp(CornerRadiusDip, 0, 24)
                : defaults.CornerRadiusDip
        };
    }
}
