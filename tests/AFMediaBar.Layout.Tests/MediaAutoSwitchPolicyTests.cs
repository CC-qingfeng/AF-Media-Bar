using System;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 自动跟随的判定：什么时候该切到另一个正在播放的来源，什么时候必须停手。
///
/// 这里的每一条都对应一个用户能看见的行为，其中前两条直接来自一次真实反馈——手动切换的来源会被自动跟随抢回去，
/// 而从浏览器切走之后永远回不来。
/// The auto-follow decision: when to switch to another playing source and when to keep hands off.
///
/// Every case here is a behaviour the user can see, and the first two come straight from real feedback: a source picked by hand was taken back by auto-follow,
/// while switching away from a browser could never be undone.
/// </summary>
[TestClass]
public sealed class MediaAutoSwitchPolicyTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private static MediaAutoSwitchSignals Signals(
        bool currentIsPlaying = false,
        bool currentIsBrowser = false,
        bool isManual = false,
        bool hasReplacement = true,
        bool replacementMatchesPending = false,
        double sincePendingSeconds = 0) =>
        new(
            CurrentExists: true,
            CurrentIsPlaying: currentIsPlaying,
            CurrentIsBrowserSource: currentIsBrowser,
            IsManualSelection: isManual,
            HasPlayingReplacement: hasReplacement,
            ReplacementMatchesPending: replacementMatchesPending,
            SincePending: TimeSpan.FromSeconds(sincePendingSeconds),
            GracePeriod: Grace);

    /// <summary>
    /// 手动选中的来源不被自动跟随抢走（用户反馈的"切到播放器源后约 0.5 秒又跳回网页源"）。
    /// A source picked by hand is never taken back by auto-follow, which is the reported "switching to the player source jumps back to the web source after about
    /// half a second".
    /// </summary>
    [TestMethod]
    public void AManualSelectionIsNeverTakenBack()
    {
        Assert.AreEqual(
            MediaAutoSwitchDecision.Stay,
            MediaAutoSwitchPolicy.Resolve(Signals(isManual: true, hasReplacement: true, replacementMatchesPending: true, sincePendingSeconds: 30)));

        // 手动选中一个正在播的来源也一样：它本来就是用户要的。
        // The same holds for a playing source picked by hand: it is what the user asked for.
        Assert.AreEqual(
            MediaAutoSwitchDecision.Stay,
            MediaAutoSwitchPolicy.Resolve(Signals(currentIsPlaying: true, isManual: true)));
    }

    /// <summary>
    /// 用户手动选中浏览器之后也不会被抢回去：反向的那一半反馈由此修好——手动选择对称地生效，不再"过去容易、回来难"。
    /// After picking the browser by hand it is not taken back either, which fixes the other half of the report: a manual choice now works symmetrically instead of
    /// "easy to switch there, impossible to switch back".
    /// </summary>
    [TestMethod]
    public void PickingTheBrowserByHandAlsoSticks()
    {
        Assert.AreEqual(
            MediaAutoSwitchDecision.Stay,
            MediaAutoSwitchPolicy.Resolve(Signals(currentIsBrowser: true, isManual: true, replacementMatchesPending: true, sincePendingSeconds: 30)));
    }

    /// <summary>自动选中浏览器来源时同样不切走：网页播放器的会话随页面重建，切走会让媒体栏来回跳。</summary>
    /// <remarks>An automatically selected browser source is left alone as well: a web player's session is recreated with its page, and switching away would make the bar jump back and forth.</remarks>
    [TestMethod]
    public void AnAutomaticallySelectedBrowserIsLeftAlone()
    {
        Assert.AreEqual(
            MediaAutoSwitchDecision.Stay,
            MediaAutoSwitchPolicy.Resolve(Signals(currentIsBrowser: true, hasReplacement: true, replacementMatchesPending: true, sincePendingSeconds: 30)));
    }

    /// <summary>当前来源正在播放时不切走。/ Nothing moves while the selected source is playing.</summary>
    [TestMethod]
    public void NothingHappensWhileTheSelectedSourcePlays()
    {
        Assert.AreEqual(MediaAutoSwitchDecision.Stay, MediaAutoSwitchPolicy.Resolve(Signals(currentIsPlaying: true)));
    }

    /// <summary>没有别的来源在播时不切走，也不排队。/ With nothing else playing there is nothing to switch to and nothing to wait for.</summary>
    [TestMethod]
    public void NothingHappensWithoutAPlayingReplacement()
    {
        Assert.AreEqual(MediaAutoSwitchDecision.Stay, MediaAutoSwitchPolicy.Resolve(Signals(hasReplacement: false)));
    }

    /// <summary>当前会话已消失时本判定什么都不做，由上层处理（会话临时缺失的宽限或重新解析）。/ A missing current session is left to the caller, which owns the missing-session grace and re-resolution.</summary>
    [TestMethod]
    public void AMissingCurrentSessionIsLeftToTheCaller()
    {
        var signals = Signals(hasReplacement: true, replacementMatchesPending: true, sincePendingSeconds: 30) with { CurrentExists = false };

        Assert.AreEqual(MediaAutoSwitchDecision.Stay, MediaAutoSwitchPolicy.Resolve(signals));
    }

    /// <summary>
    /// 自动模式下发现另一个正在播的来源：第一次只是开始等待，宽限期满了才真的切；而且等待必须连续只看到同一个候选。
    /// An automatically selected source plus another playing one: the first sighting only starts the wait, the switch happens once the grace period is over, and
    /// the wait has to keep seeing the same candidate throughout.
    /// </summary>
    [TestMethod]
    public void AnAutomaticSelectionSwitchesOnlyAfterTheGracePeriod()
    {
        Assert.AreEqual(
            MediaAutoSwitchDecision.WaitForGrace,
            MediaAutoSwitchPolicy.Resolve(Signals(replacementMatchesPending: false)));

        Assert.AreEqual(
            MediaAutoSwitchDecision.WaitForGrace,
            MediaAutoSwitchPolicy.Resolve(Signals(replacementMatchesPending: true, sincePendingSeconds: 0.5)));

        Assert.AreEqual(
            MediaAutoSwitchDecision.Switch,
            MediaAutoSwitchPolicy.Resolve(Signals(replacementMatchesPending: true, sincePendingSeconds: 1)));
    }

    /// <summary>宽限期必须是正数秒级：0 会让来源在抖动时来回跳，太大等于没有自动跟随。/ The grace period has to be positive and measured in seconds: zero makes the source flip while signals wobble and too large disables auto-follow in practice.</summary>
    [TestMethod]
    public void TheGracePeriodIsMeaningful()
    {
        static MediaAutoSwitchDecision At(TimeSpan grace) =>
            MediaAutoSwitchPolicy.Resolve(new MediaAutoSwitchSignals(
                CurrentExists: true,
                CurrentIsPlaying: false,
                CurrentIsBrowserSource: false,
                IsManualSelection: false,
                HasPlayingReplacement: true,
                ReplacementMatchesPending: true,
                SincePending: TimeSpan.FromMilliseconds(900),
                GracePeriod: grace));

        Assert.AreEqual(MediaAutoSwitchDecision.Switch, At(TimeSpan.FromMilliseconds(500)));
        Assert.AreEqual(MediaAutoSwitchDecision.WaitForGrace, At(TimeSpan.FromSeconds(1)));
    }
}
