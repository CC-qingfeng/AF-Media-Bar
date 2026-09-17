using System;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 跑马灯的滚动节奏：先停一下再滚动，滚到末尾停一下，再滚回来。速度按溢出距离换算，
/// 因此再长的标题也能在可接受的时间内看到末尾——这一点必须由策略保证，而不是交给动画时钟碰运气。
/// Marquee scroll timing: pause, scroll to the end, pause, scroll back. The speed is derived from the overflow distance,
/// so even a very long title reaches its end within an acceptable time; that has to be guaranteed here instead of left to
/// whatever an animation clock happens to do.
/// </summary>
public static class MarqueeScrollPolicy
{
    /// <summary>开始滚动前的停留时间，让用户先看清标题开头。 / Pause before scrolling starts, so the beginning of the title is readable first.</summary>
    public static readonly TimeSpan LeadInPause = TimeSpan.FromMilliseconds(800);

    /// <summary>到达末尾后的停留时间，让末尾也读得完。 / Pause at the end, so the tail is readable too.</summary>
    public static readonly TimeSpan TailPause = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// 最短滚动速度（DIP/秒）。原先按 30 DIP/秒计算单程时长，一段 700 DIP 的溢出要走二十多秒，
    /// 用户在几秒内只会看到"文字被裁住、几乎不动"，因此这里提高到 75。
    /// Slowest scroll speed in DIP per second. The previous 30 DIP/s made a 700 DIP overflow take over twenty seconds one way, so
    /// within a few seconds of watching the user only saw cut-off text that barely moved; it is 75 here.
    /// </summary>
    public const double MinimumSpeedDipPerSecond = 75;

    /// <summary>单程滚动的时长上限。溢出很长时靠提高速度兜住，而不是让它滚一分钟。 / Upper bound of one one-way scroll. A long overflow is covered by a higher speed rather than by scrolling for a minute.</summary>
    public static readonly TimeSpan MaximumTravelDuration = TimeSpan.FromSeconds(6);

    /// <summary>单程滚动的时长下限，避免很短的溢出在一瞬间跳过。 / Lower bound of one one-way scroll so a short overflow is not skipped instantly.</summary>
    public static readonly TimeSpan MinimumTravelDuration = TimeSpan.FromSeconds(1.2);

    /// <summary>单程滚动时长：按最短速度折算，并夹到上下限之间。 / One-way travel duration: derived from the minimum speed and clamped between the bounds.</summary>
    /// <param name="overflowDip">需要移动的距离（DIP），即文字宽度减去可用宽度。/ Distance to travel in DIP: text width minus available width.</param>
    public static TimeSpan CalculateTravelDuration(double overflowDip)
    {
        if (!double.IsFinite(overflowDip) || overflowDip <= 0)
            return MinimumTravelDuration;

        var seconds = overflowDip / MinimumSpeedDipPerSecond;
        if (seconds <= MinimumTravelDuration.TotalSeconds)
            return MinimumTravelDuration;
        return seconds >= MaximumTravelDuration.TotalSeconds
            ? MaximumTravelDuration
            : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>一个完整往返（停留－滚到末尾－停留－滚回起点）的时长。 / Duration of one full round trip: pause, scroll to the end, pause, scroll back.</summary>
    /// <param name="overflowDip">需要移动的距离（DIP）。/ Distance to travel in DIP.</param>
    public static TimeSpan CalculateCycleDuration(double overflowDip) =>
        LeadInPause + CalculateTravelDuration(overflowDip) + TailPause + CalculateTravelDuration(overflowDip);

    /// <summary>
    /// 给定时刻的横向偏移。返回 0 与 <c>-overflowDip</c> 之间的值，并且是连续函数：
    /// 折返处不允许跳变，否则用户会看到文字瞬间闪回。
    /// Horizontal offset at a given moment. The result stays between 0 and <c>-overflowDip</c> and is continuous: a jump at the
    /// turning point would look like the text flashing back.
    /// </summary>
    /// <param name="overflowDip">需要移动的距离（DIP）。/ Distance to travel in DIP.</param>
    /// <param name="elapsed">本次滚动开始以来经过的时间。/ Time elapsed since this scroll started.</param>
    public static double CalculateOffset(double overflowDip, TimeSpan elapsed)
    {
        if (!double.IsFinite(overflowDip) || overflowDip <= 0)
            return 0;

        var travel = CalculateTravelDuration(overflowDip);
        var cycle = CalculateCycleDuration(overflowDip);
        if (cycle <= TimeSpan.Zero)
            return 0;

        var position = elapsed.TotalSeconds % cycle.TotalSeconds;
        if (position < 0)
            position += cycle.TotalSeconds;

        var lead = LeadInPause.TotalSeconds;
        var travelSeconds = travel.TotalSeconds;
        var tail = TailPause.TotalSeconds;
        if (position < lead)
            return 0;
        if (position < lead + travelSeconds)
            return -overflowDip * (position - lead) / travelSeconds;
        if (position < lead + travelSeconds + tail)
            return -overflowDip;
        return -overflowDip * (1 - (position - lead - travelSeconds - tail) / travelSeconds);
    }
}
