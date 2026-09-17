using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>歌词时序规范化与信息行过滤的纯策略测试。 / Pure policy tests for lyric timing normalization and info-line filtering.</summary>
[TestClass]
public sealed class LyricTimingPolicyTests
{
    [TestMethod]
    public void MissingEndFallsBackToNextLineStart()
    {
        var lines = LyricTimingPolicy.Resolve(
            [
                new LyricLineDraft(1, null, "first"),
                new LyricLineDraft(5.5, null, "second")
            ],
            20);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(1, lines[0].Start, 0.0001);
        Assert.AreEqual(5.5, lines[0].End, 0.0001);
        Assert.AreEqual(5.5, lines[1].Start, 0.0001);
    }

    [TestMethod]
    public void LastLineUsesTrackDurationThenFixedTail()
    {
        var withDuration = LyricTimingPolicy.Resolve([new LyricLineDraft(10, null, "last")], 30);
        Assert.AreEqual(30, withDuration[0].End, 0.0001);

        var withoutDuration = LyricTimingPolicy.Resolve([new LyricLineDraft(10, null, "last")], 0);
        Assert.AreEqual(10 + LyricTimingPolicy.DefaultTailSeconds, withoutDuration[0].End, 0.0001);
    }

    [TestMethod]
    public void ExplicitEndWinsButDegenerateEndFallsBack()
    {
        var lines = LyricTimingPolicy.Resolve(
            [
                new LyricLineDraft(1, 3, "explicit"),
                new LyricLineDraft(4, 4, "degenerate"),
                new LyricLineDraft(9, null, "tail")
            ],
            12);

        Assert.AreEqual(3, lines[0].End, 0.0001);
        // 结束时间不晚于起点时按"缺失"处理，用下一行起点补齐。
        // An end no later than the start counts as missing and is completed from the next line's start.
        Assert.AreEqual(9, lines[1].End, 0.0001);
        Assert.AreEqual(12, lines[2].End, 0.0001);
    }

    [TestMethod]
    public void BlankAndUnsortedDraftsAreNormalized()
    {
        var lines = LyricTimingPolicy.Resolve(
            [
                new LyricLineDraft(9, null, "later"),
                new LyricLineDraft(2, null, "   "),
                new LyricLineDraft(1, null, " earlier ")
            ],
            10);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("earlier", lines[0].Text);
        Assert.AreEqual("later", lines[1].Text);
    }

    [TestMethod]
    public void WordsAreClampedIntoTheLineWindow()
    {
        var lines = LyricTimingPolicy.Resolve(
            [
                new LyricLineDraft(
                    10,
                    12,
                    "clamped",
                    [
                        new LyricWord(8, 11, "a"),
                        new LyricWord(11, 13, "b"),
                        new LyricWord(20, 21, "outside")
                    ])
            ],
            15);

        Assert.AreEqual(2, lines[0].Words.Count);
        Assert.AreEqual(10, lines[0].Words[0].Start, 0.0001);
        Assert.AreEqual(11, lines[0].Words[0].End, 0.0001);
        Assert.AreEqual(12, lines[0].Words[1].End, 0.0001);
    }

    [TestMethod]
    public void RelativeWordTimelineIsRestoredFromTheLineStart()
    {
        // QRC 形式的相对偏移：整条音节轴落在行起之前，但自身有长度。
        // A QRC-style relative offset: the whole syllable timeline sits before the line start yet spans a positive length.
        var lines = LyricTimingPolicy.Resolve(
            [
                new LyricLineDraft(
                    20,
                    22,
                    "relative",
                    [
                        new LyricWord(0, 0.5, "a"),
                        new LyricWord(0.5, 1.5, "b")
                    ])
            ],
            30);

        Assert.AreEqual(2, lines[0].Words.Count);
        Assert.AreEqual(20, lines[0].Words[0].Start, 0.0001);
        Assert.AreEqual(20.5, lines[0].Words[0].End, 0.0001);
        Assert.AreEqual(21.5, lines[0].Words[1].End, 0.0001);
    }
}
