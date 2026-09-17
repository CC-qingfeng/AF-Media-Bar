using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词格式识别的纯策略测试。
/// Pure policy tests for lyric format detection.
///
/// 这组断言同时锁住"本项目必须自己识别格式"这一决定：已引用的库版本无法识别这些输入。
/// These assertions also pin down the decision that this project detects formats itself: the referenced library version
/// cannot recognize these inputs.
/// </summary>
[TestClass]
public sealed class LyricsFormatDetectorTests
{
    [TestMethod]
    public void EachSupportedTextFormatIsRecognized()
    {
        Assert.AreEqual(LyricsRawTypes.Lrc, LyricsFormatDetector.Detect("[00:01.00]第一句\n[00:05.50]第二句"));
        Assert.AreEqual(LyricsRawTypes.Yrc, LyricsFormatDetector.Detect("[1000,2000](1000,500,0)你(1500,500,0)好"));
        Assert.AreEqual(LyricsRawTypes.Qrc, LyricsFormatDetector.Detect("[5000,1000]你(5000,400)好(5400,600)"));
        Assert.AreEqual(LyricsRawTypes.Krc, LyricsFormatDetector.Detect("[1000,2000]<0,500,0>你<500,500,0>好"));
        Assert.AreEqual(LyricsRawTypes.LyricifySyllable, LyricsFormatDetector.Detect("[0]你(1000,500)好(1500,500)"));
        Assert.AreEqual(LyricsRawTypes.LyricifyLines, LyricsFormatDetector.Detect("[1000,2000]第一句\n[5000,1500]第二句"));
    }

    [TestMethod]
    public void UnrecognizedAndEmptyInputStayUnknown()
    {
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect(null));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("   "));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("这不是歌词"));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("{\"unrelated\":true}"));
    }

    [TestMethod]
    public void QrcFullXmlIsRecognizedAndUnwrapped()
    {
        const string xml =
            "<QrcInfos><LyricInfo><Lyric_1 LyricType=\"1\" LyricContent=\"[5000,1000]你(5000,400)好(5400,600)\" /></LyricInfo></QrcInfos>";

        Assert.AreEqual(LyricsRawTypes.QrcFull, LyricsFormatDetector.Detect(xml));
        Assert.AreEqual("[5000,1000]你(5000,400)好(5400,600)", LyricsFormatDetector.TryUnwrap(xml, LyricsRawTypes.QrcFull));
    }

    [TestMethod]
    public void YrcFullJsonIsRecognizedAndUnwrapped()
    {
        const string json = "{\"yrc\":{\"lyric\":\"[1000,2000](1000,500,0)你(1500,500,0)好\"}}";

        Assert.AreEqual(LyricsRawTypes.YrcFull, LyricsFormatDetector.Detect(json));
        Assert.AreEqual("[1000,2000](1000,500,0)你(1500,500,0)好", LyricsFormatDetector.TryUnwrap(json, LyricsRawTypes.YrcFull));
    }

    [TestMethod]
    public void WrappedFormatsParseIntoLinesAfterUnwrapping()
    {
        var qrc = LyricsTextParser.Parse(
            "<QrcInfos><LyricInfo><Lyric_1 LyricType=\"1\" LyricContent=\"[5000,1000]你(5000,400)好(5400,600)\" /></LyricInfo></QrcInfos>",
            request: new LyricsRequest("Song", "Artist", "Album", 20, NetEaseSongId: null));

        Assert.AreEqual(1, qrc.Lines.Count);
        Assert.AreEqual("你好", qrc.Lines[0].Text);
        Assert.AreEqual(2, qrc.Lines[0].Words.Count);

        var yrc = LyricsTextParser.Parse(
            "{\"yrc\":{\"lyric\":\"[1000,2000](1000,500,0)你(1500,500,0)好\"}}",
            request: new LyricsRequest("Song", "Artist", "Album", 20, NetEaseSongId: null));

        Assert.AreEqual(1, yrc.Lines.Count);
        Assert.AreEqual("你好", yrc.Lines[0].Text);
        Assert.AreEqual(2, yrc.Lines[0].Words.Count);
    }
}
