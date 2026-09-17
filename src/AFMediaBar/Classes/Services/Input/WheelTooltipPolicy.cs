using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>滚轮手势的两个槽位：普通滚轮与按住共用按键后的组合滚轮。 / The two wheel slots: the plain wheel and the chord wheel held together with the shared modifier.</summary>
public enum WheelGestureSlot
{
    /// <summary>不按键的普通滚轮。 / The plain wheel with nothing held.</summary>
    Primary,

    /// <summary>按住共用按键的组合滚轮。 / The chord wheel with the shared modifier held.</summary>
    Chord
}

/// <summary>
/// 滚轮提示文案：悬停时说明"滚轮现在会做什么"，滚动后换成刚刚发生的结果。
/// Wheel tooltip text: while hovering it says what the wheel will do, and after scrolling it becomes the result that just happened.
/// </summary>
public static class WheelTooltipPolicy
{
    /// <summary>
    /// 悬停时的提示。普通滚轮与组合滚轮各说各的，因此用户不需要记住当前按键绑定。
    /// The hint shown while hovering. Plain and chord wheels each state their own action, so the user does not have to remember the
    /// current modifier binding.
    /// </summary>
    /// <param name="slot">要说明的槽位。/ Slot being described.</param>
    /// <param name="modifier">共用的组合键。/ The shared modifier.</param>
    /// <param name="actionName">该槽位当前绑定的动作名。/ Name of the action currently bound to that slot.</param>
    public static string BuildHint(WheelGestureSlot slot, InteractionModifier modifier, string actionName) =>
        slot == WheelGestureSlot.Chord
            ? $"{BuildModifierName(modifier)} + 滚轮：{actionName}"
            : $"普通滚轮：{actionName}";

    /// <summary>共用按键的显示名。/ Display name of the shared modifier.</summary>
    /// <param name="modifier">共用的组合键。/ The shared modifier.</param>
    public static string BuildModifierName(InteractionModifier modifier) => modifier switch
    {
        InteractionModifier.LeftMouseButton => "鼠标左键",
        InteractionModifier.RightMouseButton => "鼠标右键",
        _ => "Shift 键"
    };

    /// <summary>媒体栏滚轮动作的显示名。/ Display name of a media-bar wheel action.</summary>
    /// <param name="action">滚轮动作。/ Wheel action.</param>
    public static string BuildActionName(WheelAction action) => action switch
    {
        WheelAction.PreviousNext => "上一首 / 下一首",
        WheelAction.SwitchMediaSource => "切换播放器",
        WheelAction.OutputDevice => "切换输出设备",
        WheelAction.CurrentApplicationVolume => "调节当前媒体音量",
        _ => "未绑定"
    };

    /// <summary>托盘滚轮动作的显示名。/ Display name of a tray wheel action.</summary>
    /// <param name="behavior">托盘滚轮行为。/ Tray wheel behavior.</param>
    public static string BuildActionName(TrayWheelBehavior behavior) => behavior switch
    {
        TrayWheelBehavior.SwitchOutputDevice => "切换输出设备",
        TrayWheelBehavior.AdjustVolume => "调节当前媒体音量",
        _ => "未绑定"
    };

    /// <summary>
    /// 滚动之后的提示：动作名加上它的结果。没有结果（例如切歌时读不到曲名）时只显示动作名，
    /// 而不是留一条空白的"：后面什么都没有"。
    /// The tooltip after scrolling: the action name plus its result. With no result (the title is unreadable while skipping, for
    /// example) only the action name is shown instead of a dangling colon with nothing behind it.
    /// </summary>
    /// <param name="actionName">刚执行的动作名。/ Name of the action just executed.</param>
    /// <param name="detail">结果细节（曲名、媒体名、设备名或音量）。/ Result detail: track title, media name, device name, or volume.</param>
    public static string BuildResult(string actionName, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? actionName : $"{actionName}：{detail}";

    /// <summary>切歌动作的方向名：向上滚是上一首，向下滚是下一首。/ Direction name for the skip action: scrolling up is previous, scrolling down is next.</summary>
    /// <param name="delta">滚轮增量，正数表示向上滚。/ Wheel delta, where a positive value means scroll up.</param>
    public static string BuildSkipActionName(int delta) => delta > 0 ? "上一首" : "下一首";

    /// <summary>
    /// 在提示后面附上当前值。悬停时用户既要知道手势会做什么，也要知道它此刻的值，
    /// 例如「普通滚轮：切换输出设备（当前：扬声器）」；没有可读值时只留提示。
    /// Appends the current value to a hint. While hovering the user needs both what the gesture does and where it currently stands,
    /// as in "plain wheel: switch output device (now: Speakers)"; with no readable value only the hint remains.
    /// </summary>
    /// <param name="hint">已经拼好的提示文本。/ Already composed hint text.</param>
    /// <param name="currentValue">当前值（设备名或应用与音量）；不可读时为 null。/ Current value (device name, or application and volume), or null when unreadable.</param>
    public static string BuildHintWithValue(string hint, string? currentValue) =>
        string.IsNullOrWhiteSpace(currentValue) ? hint : $"{hint}（当前：{currentValue}）";
}
