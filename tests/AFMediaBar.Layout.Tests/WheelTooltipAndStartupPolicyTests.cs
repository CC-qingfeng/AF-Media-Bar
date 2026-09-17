using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 滚轮提示文案、组合键判定与开机启动项命令行。
/// Wheel tooltip text, chord-key resolution, and the run-at-startup command line.
/// </summary>
[TestClass]
public sealed class WheelTooltipAndStartupPolicyTests
{
    /// <summary>
    /// 悬停时普通与组合滚轮各说各的动作，组合那一行必须带上当前按键的名字，否则用户得自己记住绑定。
    /// While hovering, the plain and chord wheels each state their own action, and the chord line has to name the current key or the
    /// user would have to remember the binding.
    /// </summary>
    [TestMethod]
    public void HintsNameTheSlotAndTheCurrentModifier()
    {
        Assert.AreEqual(
            "普通滚轮：下一首 / 上一首",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Primary, InteractionModifier.Shift, "下一首 / 上一首"));
        Assert.AreEqual(
            "Shift 键 + 滚轮：切换播放器",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Chord, InteractionModifier.Shift, "切换播放器"));
        Assert.AreEqual(
            "鼠标右键 + 滚轮：切换播放器",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Chord, InteractionModifier.RightMouseButton, "切换播放器"));
        Assert.AreEqual("鼠标左键", WheelTooltipPolicy.BuildModifierName(InteractionModifier.LeftMouseButton));

        Assert.AreEqual("上一首 / 下一首", WheelTooltipPolicy.BuildActionName(WheelAction.PreviousNext));
        Assert.AreEqual("调节当前媒体音量", WheelTooltipPolicy.BuildActionName(TrayWheelBehavior.AdjustVolume));
        Assert.AreEqual("未绑定", WheelTooltipPolicy.BuildActionName(TrayWheelBehavior.Disabled));
        Assert.AreEqual("未绑定", WheelTooltipPolicy.BuildActionName((WheelAction)99));
    }

    /// <summary>
    /// 滚动之后提示要说结果，并且结果缺失时不能留下一个悬空的冒号。切歌方向按滚轮方向取名。
    /// After a scroll the tooltip states the result, and a missing result must not leave a dangling colon. The skip action is named
    /// after the wheel direction.
    /// </summary>
    [TestMethod]
    public void ResultsCarryTheOutcomeAndFallBackToTheActionName()
    {
        Assert.AreEqual("下一首：夜航", WheelTooltipPolicy.BuildResult("下一首", "夜航"));
        Assert.AreEqual("播放源：Spotify", WheelTooltipPolicy.BuildResult("播放源", "Spotify"));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildResult("下一首", null));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildResult("下一首", "   "));
        Assert.AreEqual("上一首", WheelTooltipPolicy.BuildSkipActionName(120));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildSkipActionName(-120));

        Assert.AreEqual(
            "普通滚轮：切换输出设备（当前：扬声器）",
            WheelTooltipPolicy.BuildHintWithValue("普通滚轮：切换输出设备", "扬声器"));
        Assert.AreEqual(
            "普通滚轮：切换输出设备",
            WheelTooltipPolicy.BuildHintWithValue("普通滚轮：切换输出设备", null));
    }

    /// <summary>
    /// 组合键判定只有一处：媒体栏与托盘各自提供输入状态，但必须得到同一个结论。
    /// The chord rule lives in one place: the media bar and the tray supply their own input state and must reach the same conclusion.
    /// </summary>
    [TestMethod]
    public void ChordHeldFollowsTheSharedModifierOnly()
    {
        var shift = GlobalInteractionSettings.Default with { Modifier = InteractionModifier.Shift };
        var left = shift with { Modifier = InteractionModifier.LeftMouseButton };
        var right = shift with { Modifier = InteractionModifier.RightMouseButton };

        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(shift, true, false, false));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(shift, false, true, true));
        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(left, false, true, false));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(left, true, false, true));
        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(right, false, false, true));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(right, true, true, false));

        // 组合键按住时解析到组合动作，松开时回到普通动作。
        // Holding the chord key resolves to the chord action and releasing it falls back to the plain one.
        Assert.AreEqual(
            shift.ChordWheelAction,
            GlobalWheelGesturePolicy.Resolve(shift, isShiftDown: true, false, false));
        Assert.AreEqual(
            shift.PrimaryWheelAction,
            GlobalWheelGesturePolicy.Resolve(shift, isShiftDown: false, false, false));
        Assert.AreEqual(
            right.TrayChordWheelAction,
            GlobalWheelGesturePolicy.ResolveTray(right, false, false, isRightButtonDown: true));
    }

    /// <summary>
    /// 启动项命令行必须加引号：安装目录可能含空格，不加引号时 Windows 会在第一个空格处断开，于是启动的是另一个路径。
    /// The startup command line has to quote the path: the installation directory can contain spaces, and without quotes Windows
    /// splits at the first space, which launches a different path.
    /// </summary>
    [TestMethod]
    public void StartupCommandLineQuotesThePathAndMatchingIgnoresDecorations()
    {
        Assert.AreEqual("\"C:\\Apps\\AF Media Bar\\AFMediaBar.exe\"",
            StartupRegistrationPolicy.BuildCommandLine("C:\\Apps\\AF Media Bar\\AFMediaBar.exe"));
        // 已经带引号的路径不会变成双重引号。
        // An already quoted path does not become double quoted.
        Assert.AreEqual("\"C:\\Apps\\AFMediaBar.exe\"",
            StartupRegistrationPolicy.BuildCommandLine("\"C:\\Apps\\AFMediaBar.exe\""));

        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"C:\\Apps\\AFMediaBar.exe\"",
            "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"c:\\apps\\afmediabar.exe\"",
            "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"C:\\Apps\\AFMediaBar.exe\" --minimized",
            "C:\\Apps\\AFMediaBar.exe"));
        // 未加引号的记录也能识别，但只比较第一个空格前的部分。
        // An unquoted entry is still recognized, comparing only the part before the first space.
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "C:\\Apps\\AFMediaBar.exe --minimized",
            "C:\\Apps\\AFMediaBar.exe"));

        Assert.IsFalse(StartupRegistrationPolicy.Matches(null, "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("  ", "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("\"C:\\Other\\AFMediaBar.exe\"", "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("\"C:\\Apps\\AFMediaBar.exe\"", "  "));
    }
}
