using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 设置页入场揭示策略的纯逻辑测试。
/// Pure-logic tests for the settings-page entrance reveal policy.
/// </summary>
[TestClass]
public sealed class SettingsRevealPolicyTests
{
    private static MotionProfile FullMotion =>
        MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: false);

    private static MotionProfile ReducedMotion =>
        MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: true, lowPerformance: false);

    private static MotionProfile InstantMotion =>
        MotionPolicy.Resolve(clientAreaAnimation: false, highContrast: false, lowPerformance: false);

    [TestMethod]
    public void Resolve_FirstBlock_StartsWithoutDelay()
    {
        var reveal = SettingsRevealPolicy.Resolve(0, FullMotion);

        Assert.IsTrue(reveal.ShouldAnimate);
        Assert.AreEqual(TimeSpan.Zero, reveal.Delay);
        Assert.AreEqual(FullMotion.StandardDuration, reveal.Duration);
        Assert.AreEqual(SettingsRevealPolicy.EntranceOffsetDip, reveal.OffsetY);
    }

    [TestMethod]
    public void Resolve_ConsecutiveBlocks_AdvanceByOneStaggerStep()
    {
        var first = SettingsRevealPolicy.Resolve(0, FullMotion);
        var second = SettingsRevealPolicy.Resolve(1, FullMotion);
        var third = SettingsRevealPolicy.Resolve(2, FullMotion);

        Assert.AreEqual(SettingsRevealPolicy.StaggerStep, second.Delay);
        Assert.AreEqual(SettingsRevealPolicy.StaggerStep * 2, third.Delay);
        Assert.IsTrue(second.Delay > first.Delay);
    }

    [TestMethod]
    public void Resolve_BlocksBeyondTheCap_StopAccumulatingDelay()
    {
        var atCap = SettingsRevealPolicy.Resolve(SettingsRevealPolicy.MaxStaggeredBlocks - 1, FullMotion);
        var pastCap = SettingsRevealPolicy.Resolve(SettingsRevealPolicy.MaxStaggeredBlocks, FullMotion);
        var farPastCap = SettingsRevealPolicy.Resolve(400, FullMotion);

        Assert.AreEqual(SettingsRevealPolicy.StaggerStep * (SettingsRevealPolicy.MaxStaggeredBlocks - 1), atCap.Delay);
        Assert.AreEqual(atCap.Delay, pastCap.Delay);
        Assert.AreEqual(atCap.Delay, farPastCap.Delay);
    }

    [TestMethod]
    public void Resolve_CappedDelay_StaysWithinTheUiMotionBudget()
    {
        var longest = SettingsRevealPolicy.Resolve(400, FullMotion);

        Assert.IsTrue(
            longest.Delay + longest.Duration <= TimeSpan.FromMilliseconds(300),
            "错峰揭示的总预算必须留在 300ms 以内，否则长列表看起来像卡顿。");
    }

    [TestMethod]
    public void StaggerBudget_MatchesTheDocumentedCap()
    {
        // 该断言把“上限由时间预算反推”这一决策钉住：块时长来自 StandardDuration，
        // 因此改动步长或块数上限时必须同时证明整页仍在预算内。
        // This pins the "cap derived from the time budget" decision: because the block duration comes from
        // StandardDuration, changing the step or the cap must still prove the whole reveal fits the budget.
        var lastStaggered = SettingsRevealPolicy.StaggerStep * (SettingsRevealPolicy.MaxStaggeredBlocks - 1);

        Assert.AreEqual(TimeSpan.FromMilliseconds(120), lastStaggered);
        Assert.IsTrue(lastStaggered + FullMotion.StandardDuration <= TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Resolve_NegativeIndex_IsTreatedAsTheFirstBlock()
    {
        var negative = SettingsRevealPolicy.Resolve(-5, FullMotion);

        Assert.IsTrue(negative.ShouldAnimate);
        Assert.AreEqual(TimeSpan.Zero, negative.Delay);
    }

    [TestMethod]
    public void Resolve_InstantMotion_ProducesNoAnimationValues()
    {
        var reveal = SettingsRevealPolicy.Resolve(3, InstantMotion);

        Assert.IsFalse(reveal.ShouldAnimate);
        Assert.AreEqual(TimeSpan.Zero, reveal.Delay);
        Assert.AreEqual(TimeSpan.Zero, reveal.Duration);
        Assert.AreEqual(0d, reveal.OffsetY);
    }

    [TestMethod]
    public void Resolve_ReducedMotion_KeepsOpacityButDropsOffsetAndStagger()
    {
        var reveal = SettingsRevealPolicy.Resolve(5, ReducedMotion);

        Assert.IsTrue(reveal.ShouldAnimate);
        Assert.AreEqual(TimeSpan.Zero, reveal.Delay);
        Assert.AreEqual(ReducedMotion.StandardDuration, reveal.Duration);
        Assert.AreEqual(0d, reveal.OffsetY);
    }
}
