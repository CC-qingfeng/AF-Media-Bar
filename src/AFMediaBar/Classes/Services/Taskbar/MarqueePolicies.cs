using System;
using System.Globalization;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 跑马灯的节奏常量。位置按**字符**计量、按**帧**推进：位置是连续的小数，窗口字符串只在整数位置跨过时改写，
/// 小数部分由渲染变换补上，因此滚动是连续的，而不是一个字一个字地跳。
/// Marquee timing constants. The position is measured in characters and advanced frame by frame: it is a continuous fraction, the
/// window string is rewritten only when the integer position crosses, and the fraction is drawn with a render transform, so the
/// scroll is continuous instead of jumping one character at a time.
/// </summary>
public static class MarqueeTiming
{
    /// <summary>推进的帧间隔。/ Frame interval of the advance.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>一个字符的停留时间：约每秒 4.5 个字，汉字读得下来，两行歌词也不会互相抢眼睛。 / Time one character takes: about 4.5 characters per second, which stays readable for CJK text without the two lyric rows competing for the eye.</summary>
    public static readonly TimeSpan CharacterDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>每帧推进的字符数。/ Characters advanced per frame.</summary>
    public static double CharactersPerFrame => FrameInterval.TotalMilliseconds / CharacterDuration.TotalMilliseconds;

    /// <summary>开始推进前保持原位的字符数，让开头的字先读完。 / Characters the text stays put before advancing, so the beginning can be read first.</summary>
    public const double LeadInCharacters = 3;
}

/// <summary>
/// 跑马灯的切分点必须落在完整字符上：代理对（emoji）、组合记号与变体选择符一旦被拆到窗口两端，
/// 各自会渲染成一个替代字形。把切分点对齐到文本元素边界即可，代价是某些轮转相位被跳过（时长上表现为多停一步）。
/// The marquee's split points always land on whole characters: a surrogate pair (emoji), a combining mark, or a variation selector split
/// across the window ends would each render as a replacement glyph. Snapping the split point to a text-element boundary avoids that at
/// the cost of skipping a rotation phase, which only shows up as one extra paused step.
/// </summary>
public static class MarqueeTextBoundary
{
    /// <summary>
    /// 把索引向前对齐到不小于它的文本元素起点；已经在边界上时原样返回，超出长度时给出长度。
    /// Snaps an index forward to the text-element start at or after it; an index already on a boundary is returned unchanged and an
    /// out-of-range index is clamped to the length.
    /// </summary>
    /// <param name="text">要切分的文字。/ Text being split.</param>
    /// <param name="index">切分点。/ Split point.</param>
    public static int SnapForward(string? text, int index)
    {
        if (string.IsNullOrEmpty(text) || index <= 0)
            return 0;

        if (index >= text.Length)
            return text.Length;

        // 快速路径：切分点上是普通字符时它必然落在元素边界上，不必解析整段文字。
        // 跑马灯每帧都要问一次，因此这条路径决定了它是否分配内存。
        // Fast path: a split point on an ordinary character is already a boundary, so the text does not have to be parsed. The marquee
        // asks once per frame, so this path decides whether it allocates at all.
        if (!MightBeInsideTextElement(text[index]))
            return index;

        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var found = Array.BinarySearch(boundaries, index);
        if (found >= 0)
            return index;

        var next = ~found;
        return next < boundaries.Length ? boundaries[next] : text.Length;
    }

    /// <summary>
    /// 该索引向后对齐到不大于它的文本元素起点；没有可用边界时给出 0。
    /// Snaps an index backward to the last text-element start at or before it, or zero when no boundary is available.
    /// </summary>
    /// <param name="text">要切分的文字。/ Text being split.</param>
    /// <param name="index">切分点。/ Split point.</param>
    public static int SnapBackward(string? text, int index)
    {
        if (string.IsNullOrEmpty(text) || index <= 0)
            return 0;

        if (index >= text.Length)
            return text.Length;

        if (!MightBeInsideTextElement(text[index]))
            return index;

        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var found = Array.BinarySearch(boundaries, index);
        if (found >= 0)
            return index;

        var previous = ~found - 1;
        return previous >= 0 ? boundaries[previous] : 0;
    }

    /// <summary>该字符能在文本元素内部出现（组合记号、格式字符或代理项）——只有这些情况才需要真正解析。/ Whether the character can appear inside a text element, which means a combining mark, a format character, or a surrogate; only these need a real parse.</summary>
    /// <param name="value">切分点上的字符。/ Character at the split point.</param>
    private static bool MightBeInsideTextElement(char value)
    {
        if (char.IsSurrogate(value))
            return true;

        return CharUnicodeInfo.GetUnicodeCategory(value) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format;
    }

    /// <summary>第一个完整文本元素（窗口开头的字符），用于换算小数位移。/ The first whole text element, which is the window's head character, used to convert the fractional offset.</summary>
    /// <param name="text">窗口文字。/ Window text.</param>
    public static string FirstElement(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : StringInfo.GetNextTextElement(text);
}

/// <summary>
/// 轮转式跑马灯：文字超宽时**轮转字符串本身**，而不是把加宽的文本用位移带着走。
///
/// 轮转的做法里根本不存在"比容器宽的元素"——窗口文字始终按容器宽度硬裁，而每个字符都会依次轮到窗口开头，
/// 因此整段文字一定都能看到；这消除了"测量是否准确、写回的宽度是否被覆盖、位移是否真的在推进"这一整类失败点。
/// Rotation marquee: an over-long text is handled by **rotating the string itself** instead of widening the TextBlock and translating
/// it.
///
/// Rotation leaves no element wider than its container: the window text is hard-cut at the container width, and every character takes
/// its turn at the window's head, so the whole text is always readable. That removes the entire class of failures around measurement
/// accuracy, the width write-back being overwritten, and whether a translation actually advances.
/// </summary>
public static class MarqueeRotationPolicy
{
    /// <summary>接缝处的视觉间隔：末尾与下一轮开头之间留出空隙，否则读者看不出哪里是结尾。 / Visual gap at the seam so the reader can tell where the text ends and the next round begins.</summary>
    public const string Separator = "   ";

    /// <summary>
    /// 轮转窗口的整数起点（已按窗口长度回绕并向前对齐到文本元素边界）。起点与连续位置之差就是小数位移，
    /// 因此它 MUST 与 <see cref="BuildWindow"/> 用到的是同一个值，否则对齐到边界的那一相位会跳一个字。
    /// Integer start of the rotation window, wrapped by the window length and snapped forward to a text-element boundary. The difference
    /// between it and the continuous position is the fractional offset, so it MUST be the same value <see cref="BuildWindow"/> uses;
    /// otherwise the phase that snapped to a boundary would jump by one character.
    /// </summary>
    /// <param name="content">原文。/ Original content.</param>
    /// <param name="offset">连续位置取整后的偏移。/ Offset rounded down from the continuous position.</param>
    public static int ResolveWindowStart(string? content, int offset)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        var text = content + Separator;
        return MarqueeTextBoundary.SnapForward(text, NormalizeOffset(offset, text.Length));
    }

    /// <summary>
    /// 构造轮转窗口：在原文后接上间隔，再整体左移 offset 个字符。offset 为 0 时窗口是"原文加间隔"。
    /// 切分点先向前对齐到文本元素边界，代理对与组合记号因此不会被拆到接缝两侧（接缝后是空格，向前对齐总能落在边界上）。
    /// Builds the rotation window: the content followed by the separator, shifted left by the offset in characters. At offset zero the
    /// window is the content plus the separator. The split point is snapped forward to a text-element boundary first, so surrogate pairs
    /// and combining marks never straddle the seam; the separator is made of spaces, so a forward boundary always exists.
    /// </summary>
    /// <param name="content">原文。/ Original content.</param>
    /// <param name="offset">已经轮转的字符数。/ Number of characters already rotated.</param>
    public static string BuildWindow(string? content, int offset)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var text = content + Separator;
        var start = ResolveWindowStart(content, offset);
        return start == 0 ? text : text[start..] + text[..start];
    }

    /// <summary>把偏移夹到窗口长度之内（含负数与越界）。/ Wraps an offset into the window length, including negative and oversized values.</summary>
    /// <param name="offset">偏移字符数。/ Offset in characters.</param>
    /// <param name="windowLength">窗口字符数。/ Window length in characters.</param>
    public static int NormalizeOffset(int offset, int windowLength) =>
        windowLength <= 0 ? 0 : ((offset % windowLength) + windowLength) % windowLength;

    /// <summary>窗口长度（原文加间隔）的字符数。/ Character count of the window, which is the content plus the separator.</summary>
    /// <param name="contentLength">原文字符数。/ Content length in characters.</param>
    public static int ResolveWindowLength(int contentLength) => contentLength <= 0 ? 0 : contentLength + Separator.Length;
}

/// <summary>
/// 跟随式跑马灯：把窗口位置解成"亮区恰好留在可见区内"，唱到哪就滚到哪。
///
/// 为什么不用轮转：轮转是循环移位，已唱段在窗口里会落在接缝两侧、变成一段跨界的弧，而裁剪矩形只能表达一段连续区间，
/// 于是要么擦亮错位要么漏掉一段。跟随式只向后丢弃已经唱过的字、永不回头，已唱段因此在窗口里**始终是前缀**，
/// 擦亮就是"从字形起点裁到已唱位置"。
/// Follow marquee: the window position is solved so the reveal stays inside the visible region, which makes the line scroll to wherever
/// the singing has got to.
///
/// Why not rotation: rotation is a cyclic shift, which turns the sung run into an arc that crosses the seam, and a clip rectangle can
/// only express one contiguous span — the reveal would either sit on the wrong characters or drop a piece. The follow mode only ever
/// discards characters that are already sung and never wraps, so the sung run stays a **prefix** of the window and the reveal is simply
/// "clip from the first glyph to the sung position".
/// </summary>
public static class MarqueeFollowPolicy
{
    /// <summary>擦亮边界距右边界保留的比例：边界进入这个范围内才继续左移，读者因此始终能看到接下来要唱的字。 / Share of the visible width kept clear at the right edge; the window only shifts once the reveal enters it, so the upcoming characters stay visible.</summary>
    public const double FollowMarginRatio = 0.2;

    /// <summary>构造跟随窗口：从 start 个字符之后开始的原文后缀。start 为 0 时就是原文本身。 / Builds the follow window: the content's tail from the given start offset. At zero it is the content itself.</summary>
    /// <param name="content">原文。/ Original content.</param>
    /// <param name="start">已经丢弃的字符数，必须已经对齐到文本元素边界（见 <see cref="SnapStart"/>）。/ Characters already discarded from the head; it has to be a text-element boundary already, see <see cref="SnapStart"/>.</param>
    public static string BuildWindow(string? content, int start)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var normalized = Math.Clamp(start, 0, content.Length);
        return normalized == 0 ? content : content[normalized..];
    }

    /// <summary>
    /// 把窗口起点对齐到文本元素边界，并夹在窗口起点上限之内：向前对齐越过上限时改用该元素自身的起点，
    /// 窗口因此最多比容器宽一个元素（尾部只放得下一个元素时的唯一选择），但任何情况下都不会把代理对或组合记号拆成两段。
    /// Snaps a window start to a text-element boundary and clamps it to the start limit: when the forward boundary would pass the limit
    /// the element's own start is used instead, which leaves the window one element wider than the container (the only option when the
    /// tail holds a single element) but never splits a surrogate pair or a combining mark.
    /// </summary>
    /// <param name="content">原文。/ Original content.</param>
    /// <param name="candidate">期望的起点。/ Desired start.</param>
    /// <param name="maximumStart">允许的最大起点。/ Largest start allowed.</param>
    public static int SnapStart(string? content, int candidate, int maximumStart)
    {
        var limit = Math.Max(0, maximumStart);
        var current = Math.Clamp(candidate, 0, limit);
        if (string.IsNullOrEmpty(content))
            return 0;

        var snapped = MarqueeTextBoundary.SnapForward(content, current);
        return snapped <= limit
            ? snapped
            : Math.Max(0, MarqueeTextBoundary.SnapBackward(content, current));
    }

    /// <summary>窗口起点允许到达的最大值：窗口至少要留下最后一个可见宽度。 / Largest start the window may reach: the last visible width always stays.</summary>
    /// <param name="contentLength">原文字符数。/ Content length in characters.</param>
    /// <param name="visibleCharacters">当前完整可见的字符数。/ Characters currently fully visible.</param>
    public static int ResolveMaximumStart(int contentLength, int visibleCharacters) =>
        Math.Max(0, contentLength - Math.Max(1, visibleCharacters));

    /// <summary>擦亮边界需要保留的字符数。/ Characters kept clear ahead of the reveal edge.</summary>
    /// <param name="visibleCharacters">当前完整可见的字符数。/ Characters currently fully visible.</param>
    public static int ResolveFollowMargin(int visibleCharacters) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, visibleCharacters) * FollowMarginRatio));

    /// <summary>
    /// 求解窗口位置：让已唱段落在窗口可见区内，并让亮区与右边界保持一个边距，即"唱到哪滚到哪"。
    ///
    /// 关键性质是**已唱段永远不越出可见区**（`sungPosition - position &lt;= visibleCharacters`）：位置按帧求解而不是每步挪一个字，
    /// 唱得快时窗口就跟得快，因此不会出现"滚动慢于亮区、高亮跑出显示范围"。整行唱完时位置停在上限，尾巴留在可见区内。
    /// Solves the window position: it keeps the sung run inside the visible region with one margin left clear at the right edge, which is
    /// what makes the line scroll to wherever the singing has got to.
    ///
    /// The property that matters is that the sung run **never leaves the visible region**
    /// (`sungPosition - position &lt;= visibleCharacters`). The position is solved every frame instead of shifting one character per step, so
    /// a fast line scrolls just as fast and the reveal can no longer run outside the window. Once the whole line is sung the position
    /// rests at its limit and the tail stays visible.
    /// </summary>
    /// <param name="sungPosition">原文中已经唱到的位置（字符，可含小数）。/ Sung position in the content, in characters and possibly fractional.</param>
    /// <param name="contentLength">原文字符数。/ Content length in characters.</param>
    /// <param name="visibleCharacters">当前完整可见的字符数。/ Characters currently fully visible.</param>
    public static double ResolvePosition(double sungPosition, int contentLength, int visibleCharacters)
    {
        if (contentLength <= 0 || !double.IsFinite(sungPosition))
            return 0;

        var visible = Math.Max(1, visibleCharacters);
        var margin = ResolveFollowMargin(visible);
        var limit = ResolveMaximumStart(contentLength, visible);
        return Math.Clamp(sungPosition - (visible - margin), 0, limit);
    }

    /// <summary>
    /// 在窗口前缀宽度表里找出"完整可见"的字符数：某个字符的前缀宽度不超过可见宽度时它才算完整可见。
    /// 二分查找，因此可以每步调用一次而不需要缓存。
    /// Finds how many characters are fully visible in a window prefix-width table: a character counts only while its prefix width stays
    /// within the visible width. The search is binary so it can run once per step without caching.
    /// </summary>
    /// <param name="prefixWidths">窗口前缀宽度表，长度是窗口字符数加一，第一项为 0。/ Window prefix widths, one longer than the window, starting at zero.</param>
    /// <param name="visibleWidth">可见宽度（容器宽度，DIP）。/ Visible width in DIP, which is the container width.</param>
    public static int ResolveVisibleCharacterCount(double[]? prefixWidths, double visibleWidth)
    {
        if (prefixWidths is null || prefixWidths.Length <= 1 || !double.IsFinite(visibleWidth) || visibleWidth <= 0)
            return 0;

        var low = 0;
        var high = prefixWidths.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (prefixWidths[middle] <= visibleWidth)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    /// <summary>
    /// 窗口内已唱段的裁剪宽度，按字符位置在相邻两个前缀之间线性插值：擦亮边界因此与文字一样连续移动，
    /// 不会在字与字之间跳。索引越界时给出窗口的整段宽度。
    /// Clip width of the sung run inside the window, interpolated between two neighbouring prefixes by character position, so the reveal
    /// edge moves as continuously as the text instead of jumping between characters. An out-of-range index yields the window's full width.
    /// </summary>
    /// <param name="prefixWidths">窗口前缀宽度表。/ Window prefix-width table.</param>
    /// <param name="sungCharacters">窗口内已唱字符位置（可含小数）。/ Sung position inside the window, possibly fractional.</param>
    public static double ResolveSungWidth(double[]? prefixWidths, double sungCharacters)
    {
        if (prefixWidths is null || prefixWidths.Length == 0 || !double.IsFinite(sungCharacters))
            return 0;

        var clamped = Math.Clamp(sungCharacters, 0, prefixWidths.Length - 1);
        var index = (int)Math.Floor(clamped);
        var next = Math.Min(index + 1, prefixWidths.Length - 1);
        return prefixWidths[index] + (clamped - index) * (prefixWidths[next] - prefixWidths[index]);
    }

    /// <summary>按擦亮进度换算原文中已唱到的位置（字符，可含小数）。/ Converts the reveal progress into the sung position in the content, in possibly fractional characters.</summary>
    /// <param name="progress">擦亮进度（0–1）。/ Reveal progress, 0–1.</param>
    /// <param name="contentLength">原文字符数。/ Content length in characters.</param>
    public static double ResolveSungPosition(double progress, int contentLength)
    {
        if (contentLength <= 0 || !double.IsFinite(progress))
            return 0;

        return Math.Clamp(progress, 0, 1) * contentLength;
    }
}
