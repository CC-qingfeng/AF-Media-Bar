namespace AFMediaBar.Classes.Settings;

/// <summary>播放器表面的全局操作方式。 / Global interaction mode for player surfaces.</summary>
public enum MediaInteractionMode
{
    Buttons = 0,
    Hybrid = 1,
    Gestures = 2
}

/// <summary>播放器表面滚轮执行的媒体动作；旧音频值仅用于设置兼容读取。 / Media action for player-surface wheel input; legacy audio values remain only for settings compatibility.</summary>
public enum WheelAction
{
    PreviousNext = 0,
    CurrentApplicationVolume = 1,
    OutputDevice = 2,
    SwitchMediaSource = 3
}

/// <summary>组合滚轮使用的鼠标按键。 / Mouse button used by a chorded wheel gesture.</summary>
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
    OpenAudioControl = 2
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
            FullPanel = FullPanel.Normalize(),
            LengthMode = Enum.IsDefined(LengthMode) ? LengthMode : defaults.LengthMode,
            FixedLengthDip = double.IsFinite(FixedLengthDip) && FixedLengthDip >= MinimumStoredFixedLengthDip
                ? Math.Clamp(FixedLengthDip, MinimumStoredFixedLengthDip, MaximumStoredFixedLengthDip)
                : defaults.FixedLengthDip
        };
    }
}

/// <summary>播放器显示模式共用的媒体交互设置。 / Media-interaction settings shared by player display modes.</summary>
public readonly record struct GlobalInteractionSettings(
    MediaInteractionMode Mode,
    WheelAction PrimaryWheelAction,
    bool ChordWheelEnabled,
    MouseChordButton ChordButton,
    WheelAction ChordWheelAction,
    TrayClickAction TrayClickAction,
    bool TrayUsesGlobalWheel)
{
    public static GlobalInteractionSettings Default { get; } = new(
        MediaInteractionMode.Hybrid,
        WheelAction.PreviousNext,
        false,
        MouseChordButton.Left,
        WheelAction.SwitchMediaSource,
        TrayClickAction.OpenAudioControl,
        false);

    public GlobalInteractionSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Mode = Enum.IsDefined(Mode) ? Mode : defaults.Mode,
            PrimaryWheelAction = NormalizePlayerWheelAction(PrimaryWheelAction, defaults.PrimaryWheelAction),
            ChordButton = Enum.IsDefined(ChordButton) ? ChordButton : defaults.ChordButton,
            ChordWheelAction = NormalizePlayerWheelAction(ChordWheelAction, defaults.ChordWheelAction),
            TrayClickAction = Enum.IsDefined(TrayClickAction) ? TrayClickAction : defaults.TrayClickAction,
            // 兼容读取 schema 4 的旧字段；托盘滚轮已恢复为独立的 TrayWheelBehavior。
            // Retain the schema-4 field for reading compatibility; tray wheel uses TrayWheelBehavior again.
            TrayUsesGlobalWheel = false
        };
    }

    private static WheelAction NormalizePlayerWheelAction(WheelAction action, WheelAction fallback) =>
        action is WheelAction.PreviousNext or WheelAction.SwitchMediaSource ? action : fallback;
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
