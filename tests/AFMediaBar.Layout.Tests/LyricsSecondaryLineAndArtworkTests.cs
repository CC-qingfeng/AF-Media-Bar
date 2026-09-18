using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 第二行歌词的取词顺序（首选项 + 固定回退链）与封面框的尺寸策略。
/// The second lyric line's source order (a preference plus a fixed fallback chain) and the artwork box sizing policy.
/// </summary>
[TestClass]
public sealed class LyricsSecondaryLineAndArtworkTests
{
    /// <summary>
    /// 首选项有内容时就用它，不因为其他来源也有内容而改变。
    /// The preferred source wins when it has content, regardless of the other sources.
    /// </summary>
    [TestMethod]
    public void PreferredSourceWinsWhenItHasContent()
    {
        Assert.AreEqual(
            "下一句",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.NextLine, "下一句", "译文", "yinyi"));
        Assert.AreEqual(
            "译文",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, "下一句", "译文", "yinyi"));
        Assert.AreEqual(
            "yinyi",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Romanization, "下一句", "译文", "yinyi"));
    }

    /// <summary>
    /// 首选项缺失时按固定顺序回退：翻译 → 音译 → 下一句。这是"优先显示翻译，其次音译，最后下一句"的实际行为。
    /// A missing preference falls back along the fixed order: translation, romanization, next line. This is what "prefer the translation, then
    /// the romanization, then the next line" actually does.
    /// </summary>
    [TestMethod]
    public void MissingPreferenceFallsBackAlongTheFixedOrder()
    {
        // 首选翻译但这一句没有译文：先退到音译。
        // The preference is the translation while this line has none: the romanization comes next.
        Assert.AreEqual(
            "yinyi",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, "下一句", null, "yinyi"));
        Assert.AreEqual(
            "下一句",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, "下一句", null, null));
        Assert.AreEqual(
            "译文",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Romanization, "下一句", "译文", null));
        Assert.AreEqual(
            "下一句",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.NextLine, "下一句", null, null));

        CollectionAssert.AreEqual(
            new[]
            {
                LyricsSecondaryLineMode.Translation,
                LyricsSecondaryLineMode.Romanization,
                LyricsSecondaryLineMode.NextLine
            },
            LyricsSecondaryLinePolicy.FallbackOrder.ToArray());
    }

    /// <summary>
    /// 所有来源都为空（或只有空白）时返回空串：调用方据此隐藏第二行，而不是留一个空白占位。
    /// All sources being empty, or whitespace only, returns an empty string so the caller hides the row instead of leaving a blank placeholder.
    /// </summary>
    [TestMethod]
    public void EmptySourcesHideTheSecondLine()
    {
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, null, null, null));
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, "  ", "", " "));
        // 空白不算内容，因此会继续回退到真的有内容的来源。
        // Whitespace is not content, so the fallback continues to a source that really has some.
        Assert.AreEqual(
            "下一句",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineMode.Translation, "下一句", "   ", null));
    }

    /// <summary>
    /// 封面框按封面比例算宽度：正方形不变、宽封面变宽、竖版变窄，且都不需要留白；超出范围才留白。
    /// The artwork box follows the cover's aspect: square stays square, a wide cover widens, a portrait narrows, none of them letterboxed;
    /// only an out-of-range aspect letterboxes.
    /// </summary>
    [TestMethod]
    public void ArtworkBoxFollowsTheCoverAspect()
    {
        var square = ArtworkBoxPolicy.Resolve(36, 600, 600);
        Assert.AreEqual(36, square.Width, 0.001);
        Assert.IsFalse(square.Letterbox);

        // 16:9 视频封面（1.78）仍在范围内：宽度按比例，不留白也不裁切。
        // A 16:9 video cover at 1.78 is still inside the range: the width follows the ratio with neither letterboxing nor cropping.
        var wide = ArtworkBoxPolicy.Resolve(36, 1920, 1080);
        Assert.AreEqual(64, wide.Width, 0.01);
        Assert.IsFalse(wide.Letterbox);

        // 21:9 超宽封面：夹到上限 1.8，因为超出的部分只能留白。
        // A 21:9 ultra-wide cover clamps to 1.8, since the rest would have to letterbox.
        var ultraWide = ArtworkBoxPolicy.Resolve(36, 2560, 1080);
        Assert.AreEqual(36 * 1.8, ultraWide.Width, 0.001);
        Assert.IsTrue(ultraWide.Letterbox);

        // 4:3 与 3:2 都在范围内：宽度按比例、不留白、也不裁切。
        // 4:3 and 3:2 are inside the range: the width follows the ratio with neither letterboxing nor cropping.
        var fourThree = ArtworkBoxPolicy.Resolve(36, 4, 3);
        Assert.AreEqual(48, fourThree.Width, 0.001);
        Assert.IsFalse(fourThree.Letterbox);

        var threeTwo = ArtworkBoxPolicy.Resolve(36, 3, 2);
        Assert.AreEqual(54, threeTwo.Width, 0.001);
        Assert.IsFalse(threeTwo.Letterbox);

        // 竖版海报 2:3：变窄但不留白。
        // A 2:3 portrait poster narrows without letterboxing.
        var portrait = ArtworkBoxPolicy.Resolve(36, 2, 3);
        Assert.AreEqual(24, portrait.Width, 0.001);
        Assert.IsFalse(portrait.Letterbox);

        // 极端细高：夹到下限 0.6 并留白。
        // An extremely tall cover clamps to 0.6 and letterboxes.
        var tall = ArtworkBoxPolicy.Resolve(36, 200, 1000);
        Assert.AreEqual(36 * 0.6, tall.Width, 0.001);
        Assert.IsTrue(tall.Letterbox);

        // 没有封面（尺寸无效）时保持正方形，占位音符居中。
        // Without artwork, an unusable size, the box stays square so the placeholder note stays centred.
        Assert.AreEqual(36, ArtworkBoxPolicy.Resolve(36, 0, 0).Width, 0.001);
        Assert.AreEqual(36, ArtworkBoxPolicy.Resolve(36, -5, 10).Width, 0.001);
        Assert.AreEqual(0, ArtworkBoxPolicy.Resolve(double.NaN, 100, 100).Width, 0.001);
    }
}
