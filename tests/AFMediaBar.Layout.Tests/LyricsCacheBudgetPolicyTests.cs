using System;
using System.Collections.Generic;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词缓存的体积估算：它要能把"纯文本"和"逐字"两类歌词分开，否则按字节封顶就形同虚设。
/// The size estimate behind the lyric cache budget: it has to separate plain-text lyrics from syllable-synced ones, otherwise a byte ceiling achieves
/// nothing.
/// </summary>
[TestClass]
public sealed class LyricsCacheBudgetPolicyTests
{
    private static LyricLine Line(string text, int wordCount = 0)
    {
        var words = new List<LyricWord>();
        for (var index = 0; index < wordCount; index++)
        {
            words.Add(new LyricWord(index, index + 0.5, "字"));
        }

        return new LyricLine(0, 1, text) { Words = words };
    }

    /// <summary>空文档与空引用都要能估算，且空文档不是"零成本"——它仍然占着对象头与来源格式串。/ Empty documents and null must both be estimable, and an empty document is not free: it still holds a header and its format string.</summary>
    [TestMethod]
    public void EmptyAndNullDocumentsAreEstimable()
    {
        Assert.AreEqual(0, LyricsCacheBudgetPolicy.EstimateBytes(null));
        Assert.IsTrue(LyricsCacheBudgetPolicy.EstimateBytes(LyricDocument.Empty) > 0);
    }

    /// <summary>行越多估算越大，而且是单调的。/ More lines cost more, monotonically.</summary>
    [TestMethod]
    public void MoreLinesCostMore()
    {
        var few = new LyricDocument([Line("a"), Line("b")], LyricsSyncType.LineSynced, "LRC");
        var many = new LyricDocument(
            [Line("a"), Line("b"), Line("c"), Line("d"), Line("e")],
            LyricsSyncType.LineSynced,
            "LRC");

        Assert.IsTrue(LyricsCacheBudgetPolicy.EstimateBytes(many) > LyricsCacheBudgetPolicy.EstimateBytes(few));
    }

    /// <summary>
    /// 同样行数下，带逐字时间轴的歌词必须明显更重——这正是"只按条数封顶"挡不住的那一类。
    /// At the same line count, a syllable-synced document must be clearly heavier, which is exactly the kind an entry-count cap cannot hold back.
    /// </summary>
    [TestMethod]
    public void SyllableTimelinesCostClearlyMoreThanPlainText()
    {
        var lines = new List<LyricLine>();
        var wordedLines = new List<LyricLine>();
        for (var index = 0; index < 20; index++)
        {
            lines.Add(Line("一行普通的歌词"));
            wordedLines.Add(Line("一行普通的歌词", wordCount: 12));
        }

        var plain = LyricsCacheBudgetPolicy.EstimateBytes(new LyricDocument(lines, LyricsSyncType.LineSynced, "LRC"));
        var worded = LyricsCacheBudgetPolicy.EstimateBytes(new LyricDocument(wordedLines, LyricsSyncType.SyllableSynced, "YRC"));

        Assert.IsTrue(worded > plain * 2, $"逐字歌词应明显更重：worded={worded}, plain={plain}");
    }

    /// <summary>译文与音译也计入估算：它们与正文同量级，不计就会低估一半。/ Translations and romanizations count too: they are the same order of magnitude as the line text, and leaving them out would underestimate by half.</summary>
    [TestMethod]
    public void TranslationsAndRomanizationsCount()
    {
        var plain = new LyricDocument([Line("歌词")], LyricsSyncType.LineSynced, "LRC");
        var annotated = new LyricDocument(
            [new LyricLine(0, 1, "歌词") { Translation = "translation", Romanization = "ge ci" }],
            LyricsSyncType.LineSynced,
            "LRC");

        Assert.IsTrue(LyricsCacheBudgetPolicy.EstimateBytes(annotated) > LyricsCacheBudgetPolicy.EstimateBytes(plain));
    }

    /// <summary>
    /// 预算必须落在"能覆盖当前曲目与近邻、又不会随播放无限增长"的范围里：太小会让每次切歌都重新取词，太大就失去意义。
    /// The budget has to sit where it covers the current track and its neighbours without growing without bound during playback: too small means refetching on
    /// every track change, too large means it does nothing.
    /// </summary>
    [TestMethod]
    public void TheBudgetStaysInASaneRange()
    {
        Assert.IsTrue(LyricsCacheBudgetPolicy.DefaultBudgetBytes >= 1 * 1024 * 1024);
        Assert.IsTrue(LyricsCacheBudgetPolicy.DefaultBudgetBytes <= 16 * 1024 * 1024);

        // 一条典型的逐字歌词（200 行 × 10 个音节）应当远小于预算，否则缓存等于只留一首歌。
        // One typical syllable-synced lyric — 200 lines of 10 syllables — has to be far below the budget, or the cache would hold a single track.
        var lines = new List<LyricLine>();
        for (var index = 0; index < 200; index++)
        {
            lines.Add(Line("这一行有十个字左右的歌词文本内容", wordCount: 10));
        }

        var typical = LyricsCacheBudgetPolicy.EstimateBytes(
            new LyricDocument(lines, LyricsSyncType.SyllableSynced, "YRC"));

        Assert.IsTrue(
            typical * 8 < LyricsCacheBudgetPolicy.DefaultBudgetBytes,
            $"典型逐字歌词应远小于预算：typical={typical}, budget={LyricsCacheBudgetPolicy.DefaultBudgetBytes}");
    }
}
