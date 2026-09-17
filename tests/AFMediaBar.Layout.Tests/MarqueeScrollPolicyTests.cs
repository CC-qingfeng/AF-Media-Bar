using System;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 跑马灯的滚动节奏：偏移必须连续、能真正走到末尾，并且再长的溢出也要在可接受时间内到达。
/// Marquee scroll timing: the offset has to be continuous, actually reach the end, and do so in an acceptable time even for a very
/// long overflow.
/// </summary>
[TestClass]
public sealed class MarqueeScrollPolicyTests
{
    [TestMethod]
    public void OffsetStartsAtZeroAndReachesTheVeryEnd()
    {
        const double overflow = 692.8;

        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(overflow, TimeSpan.Zero), 0.001);
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(overflow, TimeSpan.FromMilliseconds(500)), 0.001);

        // 单程结束的瞬间必须刚好等于 -overflow：差一点就意味着文字末尾永远差一点看不到。
        // At the end of one one-way trip the offset must equal -overflow exactly: falling short means the last characters never come
        // into view.
        var travel = MarqueeScrollPolicy.CalculateTravelDuration(overflow);
        Assert.AreEqual(-overflow, MarqueeScrollPolicy.CalculateOffset(overflow, MarqueeScrollPolicy.LeadInPause + travel), 0.001);
        Assert.AreEqual(
            -overflow,
            MarqueeScrollPolicy.CalculateOffset(overflow, MarqueeScrollPolicy.LeadInPause + travel + MarqueeScrollPolicy.TailPause),
            0.001);
    }

    /// <summary>
    /// 折返处不允许跳变：偏移是时间的连续函数，任意两个相邻采样点之间的差都不会超过该处速度允许的范围。
    /// No jump at the turn-around: the offset is continuous in time, so two neighbouring samples never differ by more than the speed
    /// there allows.
    /// </summary>
    [TestMethod]
    public void OffsetIsContinuousAcrossTheWholeCycle()
    {
        const double overflow = 300;
        var cycle = MarqueeScrollPolicy.CalculateCycleDuration(overflow);
        var travel = MarqueeScrollPolicy.CalculateTravelDuration(overflow);
        var step = TimeSpan.FromMilliseconds(16);
        var maximumDelta = overflow / travel.TotalSeconds * step.TotalSeconds * 1.5;

        var previous = MarqueeScrollPolicy.CalculateOffset(overflow, TimeSpan.Zero);
        for (var elapsed = step; elapsed <= cycle + step; elapsed += step)
        {
            var current = MarqueeScrollPolicy.CalculateOffset(overflow, elapsed);
            Assert.IsTrue(Math.Abs(current - previous) <= maximumDelta,
                $"{elapsed} 处的偏移发生了跳变：{previous:0.##} → {current:0.##}");
            Assert.IsTrue(current <= 0.001 && current >= -overflow - 0.001, "偏移必须落在 0 与 -overflow 之间。");
            previous = current;
        }

        // 循环结束时回到起点，因此下一轮从 0 重新开始，用户看到的是往复而不是闪回。
        // The cycle ends back at the start, so the next round begins at zero and the user sees a round trip rather than a flash back.
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(overflow, cycle), 0.5);
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(overflow, cycle + step), 2);
    }

    /// <summary>
    /// 无论溢出多长，末尾都必须在十秒之内出现；否则用户只会认为"文字被裁住了"。
    /// However long the overflow is, the tail has to appear within ten seconds; otherwise the user only concludes that the text is cut
    /// off.
    /// </summary>
    [TestMethod]
    public void TailArrivesWithinTenSecondsForAnyOverflow()
    {
        foreach (var overflow in new[] { 12d, 120d, 692.8, 1500d, 4000d })
        {
            var arrival = MarqueeScrollPolicy.LeadInPause + MarqueeScrollPolicy.CalculateTravelDuration(overflow);
            Assert.IsTrue(arrival <= TimeSpan.FromSeconds(7),
                $"溢出 {overflow} DIP 时末尾要等到 {arrival.TotalSeconds:0.#} 秒才出现。");
            Assert.AreEqual(
                -overflow,
                MarqueeScrollPolicy.CalculateOffset(overflow, arrival),
                0.001);
        }
    }

    [TestMethod]
    public void InvalidOverflowNeverScrolls()
    {
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(0, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(-10, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(double.NaN, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, MarqueeScrollPolicy.CalculateOffset(double.PositiveInfinity, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(
            MarqueeScrollPolicy.MinimumTravelDuration,
            MarqueeScrollPolicy.CalculateTravelDuration(double.NaN));

        // 负的经过时间（时钟回拨）按循环内的位置处理，不得给出正偏移把文字推出另一侧。
        // A negative elapsed time (clock adjustments) is wrapped inside the cycle and must never produce a positive offset that pushes
        // the text out the other side.
        Assert.IsTrue(MarqueeScrollPolicy.CalculateOffset(200, TimeSpan.FromSeconds(-3)) <= 0.001);
    }
}
