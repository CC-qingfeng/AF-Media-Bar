using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词文本解析的集成测试：类型识别、逐字时间轴、译文匹配、信息行过滤与无时间轴回落。
/// Integration tests for lyric text parsing: type detection, syllable timelines, translation matching, info-line
/// filtering, and the untimed fallback.
/// </summary>
[TestClass]
public sealed class LyricsTextParserTests
{
    private static LyricsRequest Request(string title = "Song", string artist = "Artist", double? duration = 20) =>
        new(title, artist, "Album", duration, NetEaseSongId: null);

    [TestMethod]
    public void EmptyOrUnrecognizedInputYieldsNoLines()
    {
        Assert.AreEqual(0, LyricsTextParser.Parse(null).Lines.Count);
        Assert.AreEqual(0, LyricsTextParser.Parse("   ").Lines.Count);
        Assert.AreEqual(0, LyricsTextParser.Parse("这不是歌词，只是一段说明文字。").Lines.Count);
        Assert.AreEqual(LyricsSyncType.Unknown, LyricsTextParser.Parse("这不是歌词，只是一段说明文字。").SyncType);
    }

    [TestMethod]
    public void PlainTextWithoutTimestampsProducesNoDisplayableLines()
    {
        // LRCLIB 的纯文本歌词走的就是这条路：它没有时间轴，按未命中处理，让兜底链继续。
        // Plain LRCLIB lyrics take this path: without a timeline they count as a miss so the fallback chain continues.
        var document = LyricsTextParser.Parse("这是第一行\n这是第二行", request: Request());

        Assert.AreEqual(0, document.Lines.Count);
    }

    [TestMethod]
    public void LinesWithoutTimestampsInsideTimedLyricsAreSkipped()
    {
        var document = LyricsTextParser.Parse(
            "[00:01.00]有时间的行\n没有时间的行\n[00:05.00]又有时间的行",
            request: Request(duration: 20));

        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("有时间的行", document.Lines[0].Text);
        Assert.AreEqual("又有时间的行", document.Lines[1].Text);
    }

    [TestMethod]
    public void LrcLinesCarryStartEndAndOrder()
    {
        var document = LyricsTextParser.Parse(
            "[00:01.00]第一句歌词\n[00:05.50]第二句歌词\n[00:10.00]第三句歌词",
            request: Request(duration: 20));

        Assert.AreEqual("Lrc", document.SourceFormat);
        Assert.AreEqual(3, document.Lines.Count);
        Assert.AreEqual("第一句歌词", document.Lines[0].Text);
        Assert.AreEqual(1d, document.Lines[0].Start, 0.001);
        Assert.AreEqual(5.5d, document.Lines[0].End, 0.001);
        Assert.AreEqual(10d, document.Lines[1].End, 0.001);
        Assert.AreEqual(20d, document.Lines[2].End, 0.001);
        Assert.AreEqual(LyricsSyncType.LineSynced, document.SyncType);
    }

    [TestMethod]
    public void SeparateTranslationIsMatchedByTimestamp()
    {
        var document = LyricsTextParser.Parse(
            "[00:01.00]First line\n[00:05.00]Second line",
            "[00:01.02]第一句\n[00:05.00]第二句",
            request: Request(duration: 20));

        Assert.AreEqual("第一句", document.Lines[0].Translation);
        Assert.AreEqual("第二句", document.Lines[1].Translation);
    }

    [TestMethod]
    public void TranslationOutsideToleranceIsNotCarriedForward()
    {
        var document = LyricsTextParser.Parse(
            "[00:01.00]First line\n[00:05.00]Second line",
            "[00:01.00]第一句\n[00:07.00]第二句",
            request: Request(duration: 20));

        Assert.AreEqual("第一句", document.Lines[0].Translation);
        Assert.IsNull(document.Lines[1].Translation);
    }

    [TestMethod]
    public void YrcTextProducesWordTimeline()
    {
        // 网易云 YRC 的音节时间是绝对毫秒值（与库的格式说明一致）。
        // NetEase YRC syllable times are absolute milliseconds, matching the library's format notes.
        var document = LyricsTextParser.Parse(
            "[1000,2000](1000,500,0)你(1500,500,0)好(2000,1000,0)世界\n[5000,1500](5000,700,0)再(5700,800,0)见",
            request: Request());

        Assert.AreEqual("Yrc", document.SourceFormat);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("你好世界", document.Lines[0].Text);
        Assert.AreEqual(3, document.Lines[0].Words.Count);
        Assert.AreEqual(1d, document.Lines[0].Start, 0.001);
        Assert.AreEqual(1.5d, document.Lines[0].Words[1].Start, 0.001);
        Assert.AreEqual(2d, document.Lines[0].Words[2].Start, 0.001);
        Assert.AreEqual(LyricsSyncType.SyllableSynced, document.SyncType);
    }

    [TestMethod]
    public void QrcTextProducesWordTimeline()
    {
        var document = LyricsTextParser.Parse(
            "[5000,1000]你(5000,400)好(5400,600)\n[8000,1000]再(8000,500)见(8500,500)",
            request: Request());

        Assert.AreEqual("Qrc", document.SourceFormat);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual(2, document.Lines[0].Words.Count);
        Assert.AreEqual(5d, document.Lines[0].Start, 0.001);
        Assert.AreEqual(5.4d, document.Lines[0].Words[1].Start, 0.001);
    }

    [TestMethod]
    public void KrcTextProducesWordTimeline()
    {
        var document = LyricsTextParser.Parse(
            "[1000,2000]<0,500,0>你<500,500,0>好<1000,1000,0>世界\n[5000,1500]<0,700,0>再<700,800,0>见",
            request: Request());

        Assert.AreEqual("Krc", document.SourceFormat);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("你好世界", document.Lines[0].Text);
        Assert.AreEqual(3, document.Lines[0].Words.Count);
        Assert.AreEqual(1.5d, document.Lines[0].Words[1].Start, 0.001);
    }

    [TestMethod]
    public void InfoLinesAreDroppedButRealLyricsSurvive()
    {
        var document = LyricsTextParser.Parse(
            "[00:00.00]作词 : 张三\n[00:00.50]作曲 : 李四\n" +
            "[00:02.00]第一句歌词\n[00:05.00]第二句歌词\n[00:08.00]第三句歌词\n[00:11.00]第四句歌词",
            request: Request(duration: 20));

        Assert.AreEqual(4, document.Lines.Count);
        Assert.IsTrue(document.Lines.All(line => !line.Text.Contains("作词") && !line.Text.Contains("作曲")));
    }

    [TestMethod]
    public void EveryLineFlaggedAsInfoKeepsTheLyrics()
    {
        // 安全阀：整首都像信息行时不过滤，避免启发式误判把歌词清空。
        // Safety valve: a document that looks entirely like credits is kept, so a heuristic misfire cannot empty the display.
        var document = LyricsTextParser.Parse(
            "[00:01.00]作词 : 张三\n[00:05.00]作曲 : 李四",
            request: Request(duration: 20));

        Assert.AreEqual(2, document.Lines.Count);
    }

    [TestMethod]
    public void DurationFallsBackToTheRequestWhenNotPassed()
    {
        var document = LyricsTextParser.Parse("[00:01.00]唯一一句", request: Request(duration: 33));

        Assert.AreEqual(33d, document.Lines[0].End, 0.001);
    }
}
