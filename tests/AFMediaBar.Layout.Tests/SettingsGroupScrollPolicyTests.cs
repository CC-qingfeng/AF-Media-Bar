using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 设置页分组滚动策略的纯逻辑测试：覆盖分组边界、首尾、空集合、越界与单调性。
/// Pure-logic tests for the settings group scroll policy, covering group boundaries, both ends, the empty
/// collection, out-of-range input, and monotonicity.
/// </summary>
[TestClass]
public sealed class SettingsGroupScrollPolicyTests
{
    private static readonly double[] Tops = [0d, 300d, 620d, 980d];

    [TestMethod]
    public void ResolveActiveIndex_EmptyCollection_ReturnsNoGroup()
    {
        Assert.AreEqual(-1, SettingsGroupScrollPolicy.ResolveActiveIndex([], 0d, atBottom: false));
    }

    [TestMethod]
    public void ResolveActiveIndex_AtTheTop_ReturnsTheFirstGroup()
    {
        Assert.AreEqual(0, SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, 0d, atBottom: false));
    }

    [TestMethod]
    public void ResolveActiveIndex_ActivatesTheNextGroupOnlyAfterItsBoundaryIsCrossed()
    {
        // 余量之内仍属于上一个分组：分组标题贴着视口顶端时人眼仍把它读作上一组的结尾。
        // Just inside the margin still belongs to the previous group: a header sitting on the viewport edge
        // still reads as the tail of the group above it.
        var justBefore = 300d - SettingsGroupScrollPolicy.ActivationMarginDip;
        var justAfter = 300d;

        Assert.AreEqual(0, SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, justBefore - 1d, atBottom: false));
        Assert.AreEqual(1, SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, justAfter, atBottom: false));
    }

    [TestMethod]
    public void ResolveActiveIndex_AtTheBottom_AlwaysReturnsTheLastGroup()
    {
        // 最后一个分组可能永远无法滚到视口顶端，因此到底时必须直接解析为它，否则它的标签永远点不亮。
        // The final group may never reach the viewport top, so the bottom must resolve straight to it, or its
        // tab could never become active.
        Assert.AreEqual(
            Tops.Length - 1,
            SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, verticalOffset: 500d, atBottom: true));
    }

    [TestMethod]
    public void ResolveActiveIndex_IsMonotonicInTheScrollOffset()
    {
        var previous = -1;
        for (var offset = 0d; offset <= 1600d; offset += 7d)
        {
            var active = SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, offset, atBottom: false);

            Assert.IsTrue(active >= previous, $"激活分组不能随下滚回退：offset={offset} 得到 {active}，上一次 {previous}。");
            previous = active;
        }

        Assert.AreEqual(Tops.Length - 1, previous);
    }

    [TestMethod]
    public void ResolveActiveIndex_NegativeOrNonFiniteOffset_FallsBackToTheFirstGroup()
    {
        Assert.AreEqual(0, SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, -400d, atBottom: false));
        Assert.AreEqual(0, SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, double.NaN, atBottom: false));
    }

    [TestMethod]
    public void ResolveJumpOffset_BringsTheGroupToTheTopInset()
    {
        var offset = SettingsGroupScrollPolicy.ResolveJumpOffset(
            groupTop: 620d,
            topInset: 12d,
            viewportHeight: 600d,
            extentHeight: 2000d);

        Assert.AreEqual(608d, offset);
        Assert.AreEqual(
            2,
            SettingsGroupScrollPolicy.ResolveActiveIndex(Tops, offset, atBottom: false),
            "跳转后该分组必须正好成为激活分组，否则标签与内容会互相矛盾。");
    }

    [TestMethod]
    public void ResolveJumpOffset_ClampsToTheLegalRange()
    {
        var belowZero = SettingsGroupScrollPolicy.ResolveJumpOffset(0d, 12d, 600d, 2000d);
        var beyondEnd = SettingsGroupScrollPolicy.ResolveJumpOffset(9000d, 12d, 600d, 2000d);

        Assert.AreEqual(0d, belowZero);
        Assert.AreEqual(1400d, beyondEnd);
    }

    [TestMethod]
    public void ResolveJumpOffset_NonFiniteGroupTop_ReturnsZero()
    {
        Assert.AreEqual(0d, SettingsGroupScrollPolicy.ResolveJumpOffset(double.NaN, 12d, 600d, 2000d));
    }

    [TestMethod]
    public void IsAtBottom_HandlesNoScrollRangeAndBothEnds()
    {
        Assert.IsTrue(SettingsGroupScrollPolicy.IsAtBottom(0d, viewportHeight: 900d, extentHeight: 600d));
        Assert.IsTrue(SettingsGroupScrollPolicy.IsAtBottom(1400d, viewportHeight: 600d, extentHeight: 2000d));
        Assert.IsFalse(SettingsGroupScrollPolicy.IsAtBottom(1398d, viewportHeight: 600d, extentHeight: 2000d));
    }

    [TestMethod]
    public void ResolveProgress_ReportsZeroWithNothingToScrollAndClampsBeyondTheEnd()
    {
        Assert.AreEqual(0d, SettingsGroupScrollPolicy.ResolveProgress(0d, viewportHeight: 900d, extentHeight: 600d));
        Assert.AreEqual(0.5d, SettingsGroupScrollPolicy.ResolveProgress(700d, viewportHeight: 600d, extentHeight: 2000d));
        Assert.AreEqual(1d, SettingsGroupScrollPolicy.ResolveProgress(9999d, viewportHeight: 600d, extentHeight: 2000d));
    }
}
