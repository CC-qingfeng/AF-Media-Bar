using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏自动隐藏显隐期间的冻结与稳定判定。/ Freeze and settle decisions during taskbar auto-hide motion.</summary>
[TestClass]
public sealed class TaskbarMotionPolicyTests
{
    [TestMethod]
    public void AChangedRectangleStaysMovingUntilFourStableSamplesArrive()
    {
        var monitor = new Rect(0, 0, 1920, 1080);
        var visible = Rect(0, 1032, 1920, 1080);
        var moving = Rect(0, 1040, 1920, 1088);
        var state = TaskbarMotionPolicy.Observe(default, visible, monitor, LayoutOrientation.Horizontal);
        Assert.IsTrue(state.IsMoving, "the first observation waits for a stable window instead of writing geometry immediately");

        state = TaskbarMotionPolicy.Observe(state, moving, monitor, LayoutOrientation.Horizontal);
        Assert.IsTrue(state.IsMoving);

        for (var sample = 0; sample < TaskbarMotionPolicy.RequiredStableSamples - 1; sample++)
        {
            state = TaskbarMotionPolicy.Observe(state, moving, monitor, LayoutOrientation.Horizontal);
            Assert.IsTrue(state.IsMoving, $"sample {sample + 1} must still reject interaction");
        }

        state = TaskbarMotionPolicy.Observe(state, moving, monitor, LayoutOrientation.Horizontal);
        Assert.IsFalse(state.IsMoving);
    }

    [TestMethod]
    public void OnlyTheEdgeTriggerStripCountsAsHidden()
    {
        var monitor = new Rect(0, 0, 1920, 1080);

        Assert.IsTrue(TaskbarMotionPolicy.IsHidden(
            Rect(0, 1078, 1920, 1126), monitor, LayoutOrientation.Horizontal));
        Assert.IsFalse(TaskbarMotionPolicy.IsHidden(
            Rect(0, 1032, 1920, 1080), monitor, LayoutOrientation.Horizontal));
        Assert.IsTrue(TaskbarMotionPolicy.IsHidden(
            Rect(-46, 0, 2, 1080), monitor, LayoutOrientation.Vertical));
        Assert.IsFalse(TaskbarMotionPolicy.IsHidden(
            Rect(0, 0, 48, 1080), monitor, LayoutOrientation.Vertical));
    }

    /// <summary>
    /// 宿主窗口显隐的判定：任务栏**稳定收起**时 MUST 由宿主自己隐藏窗口（只靠"子窗口跟着父窗口运动"偶尔会失败，
    /// 随后展开时留在原地的那条媒体栏会被任务栏边缘裁掉）；**从收起转为展开**时立刻恢复显示，不等动画稳定；
    /// 收起动画进行中保持静置层自己的判据（父窗口正带着我们走）。
    /// The host window's visibility rule: while the taskbar is **settled at the screen edge** the host MUST hide its own window (relying only on
    /// "the child follows its parent" occasionally fails, and the bar left behind is then clipped by the taskbar edge on reveal); the moment the
    /// taskbar starts **revealing** the bar is shown again without waiting for the motion to settle; and while the hide animation runs the rest
    /// layer's own rule stands, because the parent is carrying us.
    /// </summary>
    [TestMethod]
    public void HostWindowHidesWithASettledTaskbarAndReturnsAsSoonAsItReveals()
    {
        Assert.AreEqual(
            TaskbarHostVisibility.Collapsed,
            TaskbarHostVisibilityPolicy.Resolve(Hidden(), restLayerEmpty: false),
            "a settled hidden taskbar must hide the host window itself, not rely on the child following its parent");

        Assert.AreEqual(
            TaskbarHostVisibility.Visible,
            TaskbarHostVisibilityPolicy.Resolve(Revealing(), restLayerEmpty: false),
            "the reveal must not wait for the motion to settle, otherwise the bar appears a beat after the taskbar");

        Assert.AreEqual(
            TaskbarHostVisibility.Visible,
            TaskbarHostVisibilityPolicy.Resolve(Hiding(), restLayerEmpty: false),
            "while the hide animation runs the bar rides along with its parent and must not be collapsed mid-motion");

        Assert.AreEqual(
            TaskbarHostVisibility.Visible,
            TaskbarHostVisibilityPolicy.Resolve(Settled(), restLayerEmpty: false));
    }

    /// <summary>静置层一个组件都不显示时窗口始终收起，无论任务栏处于哪种状态。/ With nothing visible in the rest layer the window stays collapsed in every taskbar state.</summary>
    [TestMethod]
    public void AnEmptyRestLayerKeepsTheWindowHiddenInEveryMotionState()
    {
        Assert.AreEqual(TaskbarHostVisibility.Collapsed, TaskbarHostVisibilityPolicy.Resolve(Settled(), restLayerEmpty: true));
        Assert.AreEqual(TaskbarHostVisibility.Collapsed, TaskbarHostVisibilityPolicy.Resolve(Hiding(), restLayerEmpty: true));
        Assert.AreEqual(TaskbarHostVisibility.Collapsed, TaskbarHostVisibilityPolicy.Resolve(Revealing(), restLayerEmpty: true));
        Assert.AreEqual(TaskbarHostVisibility.Collapsed, TaskbarHostVisibilityPolicy.Resolve(Hidden(), restLayerEmpty: true));
    }

    /// <summary>任务栏稳定在屏幕上、且静置层有内容：窗口显示。/ A taskbar settled on screen with a non-empty rest layer shows the window.</summary>
    private static TaskbarMotionState Settled() => new(true, Rect(0, 1032, 1920, 1080), 4, false, false);

    /// <summary>任务栏收起动画进行中（还没判定为已隐藏）。/ The taskbar is animating towards the edge and not yet judged hidden.</summary>
    private static TaskbarMotionState Hiding() => new(true, Rect(0, 1050, 1920, 1098), 0, true, false);

    /// <summary>任务栏正在从收起状态展开：已经在移动，但仍带着"上一帧是收起"的标记。/ The taskbar is revealing: it is moving while still carrying the previous hidden flag.</summary>
    private static TaskbarMotionState Revealing() => new(true, Rect(0, 1060, 1920, 1108), 0, true, true);

    /// <summary>任务栏稳定收起在屏幕边缘。/ The taskbar is settled at the screen edge.</summary>
    private static TaskbarMotionState Hidden() => new(true, Rect(0, 1078, 1920, 1126), 4, false, true);

    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom
    };
}
