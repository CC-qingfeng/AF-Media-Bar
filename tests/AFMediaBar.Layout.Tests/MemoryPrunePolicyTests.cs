using System;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 后台剪枝的档位判定：哪些信号把进程压到哪一档，以及什么时候值得再执行一次深度回收。
/// The prune level decision: which signals push the process to which level, and when a deep reclaim is worth running again.
/// </summary>
[TestClass]
public sealed class MemoryPrunePolicyTests
{
    /// <summary>一切正常（有媒体、用户刚操作过）：不剪枝。/ Nothing to prune while media plays and the user is active.</summary>
    [TestMethod]
    public void ActivePlaybackAndActiveUserStayAtNone()
    {
        var level = MemoryPrunePolicy.Resolve(new MemoryPruneSignals(
            IsSuspended: false,
            IsSessionLocked: false,
            IsDisplayOff: false,
            SinceLastMedia: TimeSpan.FromSeconds(20),
            UserIdle: TimeSpan.FromSeconds(30)));

        Assert.AreEqual(MemoryPruneLevel.None, level);
    }

    /// <summary>
    /// 空闲档要求**媒体与用户同时空闲**：只满足一边都不算。
    /// 用户还在用电脑时清掉封面与歌词缓存，只会让下一次切歌重新联网，得不偿失；而媒体还在播时清缓存更是明显的倒退。
    /// The idle level requires **both** media and the user to be idle; either one alone is not enough. Dropping the artwork and lyric caches while the
    /// user is still at the machine only makes the next track fetch them again, and doing it while media is playing would be a clear regression.
    /// </summary>
    [TestMethod]
    public void IdleRequiresBothMediaIdleAndUserIdle()
    {
        var mediaIdle = MemoryPrunePolicy.MediaIdleThreshold + TimeSpan.FromSeconds(1);
        var userIdle = MemoryPrunePolicy.UserIdleThreshold + TimeSpan.FromSeconds(1);

        Assert.AreEqual(
            MemoryPruneLevel.Idle,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, false, false, mediaIdle, userIdle)));

        // 用户刚回来（有输入）：即使媒体早已停止也不进空闲档。
        // The user has just come back, so no idle level even though media stopped long ago.
        Assert.AreEqual(
            MemoryPruneLevel.None,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, false, false, mediaIdle, TimeSpan.FromSeconds(5))));

        // 媒体还在（或刚刚还在）：即使用户离开很久也不进空闲档。
        // Media is still present, so no idle level even though the user left long ago.
        Assert.AreEqual(
            MemoryPruneLevel.None,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, false, false, TimeSpan.FromSeconds(30), userIdle)));
    }

    /// <summary>阈值本身是边界：正好等于阈值即视为空闲，差一秒都不算。/ The thresholds are boundaries: exactly at them counts as idle, one second short does not.</summary>
    [TestMethod]
    public void IdleThresholdsAreInclusive()
    {
        Assert.AreEqual(
            MemoryPruneLevel.Idle,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(
                false,
                false,
                false,
                MemoryPrunePolicy.MediaIdleThreshold,
                MemoryPrunePolicy.UserIdleThreshold)));

        Assert.AreEqual(
            MemoryPruneLevel.None,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(
                false,
                false,
                false,
                MemoryPrunePolicy.MediaIdleThreshold - TimeSpan.FromMilliseconds(1),
                MemoryPrunePolicy.UserIdleThreshold)));
    }

    /// <summary>
    /// 优先级：睡眠高于显示关闭，显示关闭高于空闲；三者同时成立时取最高的那一档。
    /// Priority: suspending outranks a closed display, which outranks idle; when all three hold, the deepest one wins.
    /// </summary>
    [TestMethod]
    public void DeeperSignalsOutrankIdle()
    {
        var longIdle = TimeSpan.FromHours(3);

        Assert.AreEqual(
            MemoryPruneLevel.DisplayOff,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, false, true, longIdle, longIdle)));

        Assert.AreEqual(
            MemoryPruneLevel.DisplayOff,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, true, false, longIdle, longIdle)));

        Assert.AreEqual(
            MemoryPruneLevel.DisplayOff,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, true, true, longIdle, longIdle)));

        Assert.AreEqual(
            MemoryPruneLevel.Suspended,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(true, true, true, longIdle, longIdle)));
    }

    /// <summary>锁屏与关屏处于同一档：两者的可见后果相同（没人看得见这个进程）。/ A locked session and a dark display share a level, because their visible consequence is the same: nobody can see this process.</summary>
    [TestMethod]
    public void LockedSessionAndDarkDisplayShareTheSameLevel()
    {
        Assert.AreEqual(
            MemoryPruneLevel.DisplayOff,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, true, false, TimeSpan.Zero, TimeSpan.Zero)));
        Assert.AreEqual(
            MemoryPruneLevel.DisplayOff,
            MemoryPrunePolicy.Resolve(new MemoryPruneSignals(false, false, true, TimeSpan.Zero, TimeSpan.Zero)));
    }

    /// <summary>档位变化一定执行（包括恢复到常规运行）。/ A level change always runs, the return to normal operation included.</summary>
    [TestMethod]
    public void AnyLevelChangeAppliesImmediately()
    {
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.Idle, MemoryPruneLevel.None, TimeSpan.Zero));
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.DisplayOff, MemoryPruneLevel.Idle, TimeSpan.Zero));
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.None, MemoryPruneLevel.Suspended, TimeSpan.Zero));
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.Suspended, MemoryPruneLevel.DisplayOff, TimeSpan.Zero));
    }

    /// <summary>
    /// 档位没变时的重复执行：只有会真正回收工作集的两档值得按冷却重做，空闲档不重做（再清一次缓存没有任何效果）。
    /// Repeating while the level is unchanged: only the two levels that really reclaim the working set are worth redoing on the cooldown, while the
    /// idle level is not, because dropping the caches again achieves nothing.
    /// </summary>
    [TestMethod]
    public void RepeatingIsLimitedToTheDeepLevelsAndTheCooldown()
    {
        var withinCooldown = MemoryPrunePolicy.MinimumReapply - TimeSpan.FromSeconds(1);
        var pastCooldown = MemoryPrunePolicy.MinimumReapply + TimeSpan.FromSeconds(1);

        Assert.IsFalse(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.None, MemoryPruneLevel.None, pastCooldown));
        Assert.IsFalse(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.Idle, MemoryPruneLevel.Idle, pastCooldown));
        Assert.IsFalse(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.DisplayOff, MemoryPruneLevel.DisplayOff, withinCooldown));
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.DisplayOff, MemoryPruneLevel.DisplayOff, pastCooldown));
        Assert.IsTrue(MemoryPrunePolicy.ShouldApply(MemoryPruneLevel.Suspended, MemoryPruneLevel.Suspended, pastCooldown));
    }

    /// <summary>
    /// 评估周期随档位变化：剪枝后要变短（"用户回来了"没有广播，只靠轮询看得见），睡眠时反而最长（机器已在睡，不该再定时醒来）。
    /// The evaluation period follows the level: it shrinks once pruned, because "the user is back" has no broadcast and is only visible by polling, and it
    /// is longest while suspending, because the machine is already asleep and the process should stop waking up on a timer.
    /// </summary>
    [TestMethod]
    public void EvaluationPeriodShrinksWhilePrunedAndGrowsWhileSuspending()
    {
        var active = MemoryPrunePolicy.EvaluationIntervalFor(MemoryPruneLevel.None);
        var idle = MemoryPrunePolicy.EvaluationIntervalFor(MemoryPruneLevel.Idle);
        var displayOff = MemoryPrunePolicy.EvaluationIntervalFor(MemoryPruneLevel.DisplayOff);
        var suspended = MemoryPrunePolicy.EvaluationIntervalFor(MemoryPruneLevel.Suspended);

        Assert.AreEqual(MemoryPrunePolicy.EvaluationInterval, active);
        Assert.AreEqual(MemoryPrunePolicy.PrunedEvaluationInterval, idle);
        Assert.IsTrue(idle < active);
        Assert.IsTrue(displayOff > idle);
        Assert.IsTrue(suspended >= displayOff);
    }

    /// <summary>档位取值必须单调递增，否则"档位更高就更彻底"的判定会反过来。/ The level values have to increase monotonically, otherwise "a deeper level reclaims more" inverts.</summary>
    [TestMethod]
    public void LevelsAreOrderedByDepth()
    {
        Assert.IsTrue(MemoryPruneLevel.None < MemoryPruneLevel.Idle);
        Assert.IsTrue(MemoryPruneLevel.Idle < MemoryPruneLevel.DisplayOff);
        Assert.IsTrue(MemoryPruneLevel.DisplayOff < MemoryPruneLevel.Suspended);
        Assert.AreEqual(0, (int)MemoryPruneLevel.None);
    }
}
