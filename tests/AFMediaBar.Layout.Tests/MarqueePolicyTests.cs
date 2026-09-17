using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 跑马灯的两种推进方式：轮转式（标题、歌手、第二行歌词、没有逐字时间轴的歌词行）与跟随式（正在逐字擦亮的歌词行）。
/// The marquee's two advance modes: rotation for the title, artist, second lyric row, and lyric lines without a syllable timeline, and
/// the follow mode for the lyric line currently being revealed.
/// </summary>
[TestClass]
public sealed class MarqueePolicyTests
{
    [TestMethod]
    public void RotationWindowWrapsTheContentAndKeepsTheSeparatorAtTheSeam()
    {
        const string content = "ABCDE";

        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 0));
        Assert.AreEqual("BCDE   A", MarqueeRotationPolicy.BuildWindow(content, 1));
        Assert.AreEqual("DE   ABC", MarqueeRotationPolicy.BuildWindow(content, 3));
        // 偏移越界与负数都按窗口长度回绕，滚动因此不会走出字符串之外。
        // Out-of-range and negative offsets wrap by the window length, so the scroll can never leave the string.
        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 8));
        // 逆向偏移等价于正向回绕：-1 与 7 是同一个窗口位置。
        // A negative offset is the same window position as its positive wrap: -1 and 7 agree.
        Assert.AreEqual(" ABCDE  ", MarqueeRotationPolicy.BuildWindow(content, -1));
        Assert.AreEqual(
            MarqueeRotationPolicy.BuildWindow(content, -1),
            MarqueeRotationPolicy.BuildWindow(content, 7));
        Assert.AreEqual(string.Empty, MarqueeRotationPolicy.BuildWindow(string.Empty, 3));
        Assert.AreEqual(8, MarqueeRotationPolicy.ResolveWindowLength(content.Length));
    }

    /// <summary>
    /// 轮转窗口必须与原文等长（全是同一段文字的轮换），否则每转一轮文字就会缩短或重排，
    /// 而这正是"看久了标题变了样"的来源。
    /// A rotation window has to be exactly as long as the content plus the separator, because otherwise the text would shrink or
    /// rearrange once per round — which is exactly how a title starts looking wrong after a while.
    /// </summary>
    [TestMethod]
    public void RotationWindowAlwaysKeepsEveryCharacter()
    {
        const string content = "夜航 星のうた";
        var windowLength = MarqueeRotationPolicy.ResolveWindowLength(content.Length);
        for (var offset = 0; offset < windowLength * 2; offset++)
        {
            var window = MarqueeRotationPolicy.BuildWindow(content, offset);
            Assert.AreEqual(windowLength, window.Length);
            CollectionAssert.AreEquivalent(
                (content + MarqueeRotationPolicy.Separator).ToCharArray(),
                window.ToCharArray());
        }
    }

    /// <summary>
    /// 跟随式窗口是原文的一个后缀：只丢弃已经唱过的字，永不回头，因此已唱段在窗口里始终是前缀。
    /// A follow window is a suffix of the content: it only discards characters that are already sung and never wraps, so the sung run
    /// stays a prefix of the window.
    /// </summary>
    [TestMethod]
    public void FollowWindowIsAlwaysASuffixOfTheContent()
    {
        const string content = "夜航星を歌うよ";

        Assert.AreEqual(content, MarqueeFollowPolicy.BuildWindow(content, 0));
        Assert.AreEqual("を歌うよ", MarqueeFollowPolicy.BuildWindow(content, 3));
        Assert.AreEqual("よ", MarqueeFollowPolicy.BuildWindow(content, content.Length - 1));
        Assert.AreEqual(string.Empty, MarqueeFollowPolicy.BuildWindow(content, content.Length + 5));
        Assert.AreEqual(string.Empty, MarqueeFollowPolicy.BuildWindow(null, 2));

        for (var start = 0; start <= content.Length; start++)
        {
            Assert.IsTrue(content.EndsWith(MarqueeFollowPolicy.BuildWindow(content, start), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 擦亮贴近右边界才继续滚动：只要已唱段还在可见区左边，窗口就停住，读者因此能盯着当前的字。
    /// The window keeps still while the sung run is still comfortably inside the visible region, so the reader can keep their eyes on the
    /// current characters.
    /// </summary>
    [TestMethod]
    public void FollowWindowOnlyMovesWhenTheRevealReachesTheRightMargin()
    {
        const int contentLength = 40;
        const int visible = 10;
        var margin = MarqueeFollowPolicy.ResolveFollowMargin(visible);
        var limit = MarqueeFollowPolicy.ResolveMaximumStart(contentLength, visible);

        Assert.AreEqual(2, margin);
        Assert.AreEqual(30, limit);

        // 擦亮还在可见区左边：位置保持在原文开头。
        // The reveal is still on the left of the visible region: the position stays at the content's head.
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolvePosition(4, contentLength, visible), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolvePosition(visible - margin, contentLength, visible), 0.0001);

        // 擦亮进入右边距：窗口跟着走，且走得是连续的（不是一次一个字）。
        // The reveal has entered the right margin: the window follows, and it follows continuously rather than a character at a time.
        Assert.AreEqual(0.5, MarqueeFollowPolicy.ResolvePosition(visible - margin + 0.5, contentLength, visible), 0.0001);
        Assert.AreEqual(3, MarqueeFollowPolicy.ResolvePosition(visible - margin + 3, contentLength, visible), 0.0001);

        // 整行唱完：位置停在上限，尾巴留在可见区内。
        // A fully sung line rests at the limit with its tail inside the visible region.
        Assert.AreEqual(limit, MarqueeFollowPolicy.ResolvePosition(contentLength, contentLength, visible), 0.0001);

        // 窗口长度不超过容器时（文字放得下）根本不进入跟随式。
        // A window no longer than its container never needs the follow mode at all.
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveMaximumStart(5, 10));
    }

    /// <summary>
    /// 已唱段永远不允许越出窗口的可见区：这是"滚动慢于高亮、高亮跑出显示范围"那条反馈的回归线——
    /// 位置必须按亮区求解，而不是每 220 毫秒挪一个字。
    /// The sung run is never allowed to leave the window's visible region: this is the regression line for the report that the scroll fell
    /// behind the reveal and the highlight left the visible area. The position has to be solved from the reveal instead of shifting one
    /// character per 220 ms.
    /// </summary>
    [TestMethod]
    public void SungRunNeverLeavesTheVisibleRegion()
    {
        const int contentLength = 60;
        for (var visible = 1; visible <= 40; visible++)
        {
            var limit = MarqueeFollowPolicy.ResolveMaximumStart(contentLength, visible);
            for (var step = 0; step <= contentLength * 4; step++)
            {
                // 亮区位置连续推进（模拟按帧求解），并在两端之外取值。
                // The reveal advances continuously, as it does when it is solved per frame, including values beyond both ends.
                var sung = step / 4d - 1;
                var position = MarqueeFollowPolicy.ResolvePosition(sung, contentLength, visible);
                Assert.IsTrue(position >= 0, "位置不允许为负 / the position never goes negative");
                Assert.IsTrue(position <= limit, "位置不允许越过上限 / the position never passes its limit");

                var revealed = sung - position;
                Assert.IsTrue(
                    revealed <= visible,
                    $"已唱段越出可见区：sung={sung} position={position} visible={visible} / the sung run left the visible region");
                Assert.IsTrue(revealed <= limit + visible, "已唱段不允许超过整行 / the sung run never exceeds the line");
            }
        }
    }

    /// <summary>
    /// 位置按帧推进：每帧的字符数由帧间隔与单字停留时间决定，且远小于一个字（否则又回到"一个字一个字地跳"）。
    /// The position advances per frame: the per-frame character count follows from the frame interval and the per-character duration and is
    /// far below one character, which is what keeps the scroll from jumping a character at a time.
    /// </summary>
    [TestMethod]
    public void PositionAdvancesInSmallContinuousSteps()
    {
        const double perFrame = 16d / 220d;
        Assert.AreEqual(perFrame, MarqueeTiming.CharactersPerFrame, 0.0001);
        Assert.IsTrue(MarqueeTiming.CharactersPerFrame < 0.1, "每帧推进必须远小于一个字 / a frame must advance far less than a character");
        Assert.IsTrue(MarqueeTiming.CharactersPerFrame > 0, "每帧推进必须大于零 / a frame has to advance something");
    }

    /// <summary>
    /// 前缀宽度表：完整可见的字符数与已唱段的裁剪宽度都从这里读，二分查找必须落在那一段之内；
    /// 裁剪宽度按小数位置插值，因此擦亮边界与文字一样连续移动。
    /// The prefix-width table feeds both the visible character count and the sung clip width; the binary search has to land inside the run,
    /// and the clip width is interpolated by fractional position so the reveal edge moves as continuously as the text.
    /// </summary>
    [TestMethod]
    public void PrefixWidthsDriveTheVisibleCountAndTheSungWidth()
    {
        // 每个字符 10 DIP，窗口 8 个字符。
        // Ten DIP per character, eight characters in the window.
        var prefixWidths = new double[9];
        for (var index = 0; index < prefixWidths.Length; index++)
            prefixWidths[index] = index * 10;

        Assert.AreEqual(5, MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, 50));
        Assert.AreEqual(5, MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, 59.9));
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, 5));
        Assert.AreEqual(8, MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, 500));
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveVisibleCharacterCount(null, 50));
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, double.NaN));

        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungWidth(prefixWidths, 0), 0.001);
        Assert.AreEqual(30, MarqueeFollowPolicy.ResolveSungWidth(prefixWidths, 3), 0.001);
        // 小数位置在相邻两个前缀之间插值：擦亮不会在字与字之间跳。
        // A fractional position interpolates between neighbouring prefixes, so the reveal never jumps between characters.
        Assert.AreEqual(25, MarqueeFollowPolicy.ResolveSungWidth(prefixWidths, 2.5), 0.001);
        Assert.AreEqual(32.5, MarqueeFollowPolicy.ResolveSungWidth(prefixWidths, 3.25), 0.001);
        // 越界索引给出窗口整段宽度，而不是越界异常。
        // An out-of-range index yields the window's full width instead of throwing.
        Assert.AreEqual(80, MarqueeFollowPolicy.ResolveSungWidth(prefixWidths, 99), 0.001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungWidth(null, 3), 0.001);
    }

    [TestMethod]
    public void SungPositionFollowsTheRevealProgress()
    {
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(0, 20), 0.0001);
        Assert.AreEqual(5, MarqueeFollowPolicy.ResolveSungPosition(0.25, 20), 0.0001);
        // 已唱位置是连续的：按帧采样时不会只落在整字上。
        // The sung position is continuous: sampled per frame it does not have to land on whole characters.
        Assert.AreEqual(5.5, MarqueeFollowPolicy.ResolveSungPosition(0.275, 20), 0.0001);
        Assert.AreEqual(20, MarqueeFollowPolicy.ResolveSungPosition(1, 20), 0.0001);
        Assert.AreEqual(20, MarqueeFollowPolicy.ResolveSungPosition(2, 20), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(double.NaN, 20), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(0.5, 0), 0.0001);
        // 进度只前进不后退，因此已唱位置也单调不减。
        // Progress only moves forward, so the sung position never decreases either.
        var previous = 0d;
        for (var step = 0; step <= 100; step++)
        {
            var sung = MarqueeFollowPolicy.ResolveSungPosition(step / 100d, 20);
            Assert.IsTrue(sung >= previous);
            previous = sung;
        }
    }

    /// <summary>
    /// 切分点必须落在完整字符上：emoji（代理对）与带变体选择符的字被拆到窗口两端会各渲染成一个替代字形，
    /// 这是轮转相对"整段文字平移"新引入的风险，两条推进路径都要挡住。
    /// Split points always land on whole characters: an emoji (surrogate pair) or a character with a variation selector split across the
    /// window ends renders as replacement glyphs, which is a risk rotation newly introduced over translating the whole text, so both
    /// advance modes have to block it.
    /// </summary>
    [TestMethod]
    public void SplitPointsNeverBreakASurrogatePairOrACombiningMark()
    {
        const string content = "夜🌟航☺\uFE0F星";
        var contentElements = TextElements(content);

        // 轮转：每个偏移给出的窗口都是同一批完整文本元素的轮换，没有半个字符落在接缝两侧。
        // Rotation: every offset yields a rotation of the same whole text elements, with no half character at the seam.
        var sourceElements = TextElements(content + MarqueeRotationPolicy.Separator);
        var windowLength = MarqueeRotationPolicy.ResolveWindowLength(content.Length);
        for (var offset = 0; offset < windowLength * 2; offset++)
        {
            CollectionAssert.AreEquivalent(
                sourceElements,
                TextElements(MarqueeRotationPolicy.BuildWindow(content, offset)));
        }

        // 跟随：起点只向前对齐到元素边界，因此既不拆字也不后退，并且只在起点上限处停下。
        // Following: the start only snaps forward to an element boundary, so it neither splits nor moves backwards, and it stops only at
        // the start limit.
        const int visible = 3;
        var limit = MarqueeFollowPolicy.ResolveMaximumStart(content.Length, visible);
        var start = 0;
        for (var step = 0; step < content.Length + 5; step++)
        {
            var next = MarqueeFollowPolicy.SnapStart(content, start + 1, limit);
            Assert.IsTrue(next >= start, "窗口起点不允许后退 / the window start never moves backwards");
            Assert.IsTrue(next <= limit, "窗口起点不允许越过上限 / the window start never passes its limit");
            Assert.IsTrue(next > start || next == limit, "只允许在起点上限处停下 / only the start limit may stop the window");
            Assert.AreEqual(
                string.Concat(contentElements.Skip(CountTextElementsBefore(content, next))),
                MarqueeFollowPolicy.BuildWindow(content, next));
            start = next;
        }

        Assert.AreEqual(limit, start, "全部唱完后窗口滑到起点上限 / a fully sung line reaches the start limit");
    }

    /// <summary>按文本元素切分文字（emoji 与组合记号各算一个元素）。/ Splits text into text elements, counting an emoji or a combining mark as one.</summary>
    private static List<string> TextElements(string text)
    {
        var elements = new List<string>();
        for (var index = 0; index < text.Length;)
        {
            var element = StringInfo.GetNextTextElement(text, index);
            elements.Add(element);
            index += element.Length;
        }

        return elements;
    }

    /// <summary>索引之前有多少个完整文本元素。/ How many whole text elements precede the index.</summary>
    private static int CountTextElementsBefore(string text, int index)
    {
        var count = 0;
        for (var position = 0; position < index && position < text.Length; count++)
            position += StringInfo.GetNextTextElement(text, position).Length;

        return count;
    }
}
