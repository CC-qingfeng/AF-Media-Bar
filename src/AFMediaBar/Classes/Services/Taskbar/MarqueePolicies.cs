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
/// 跟随式跑马灯：按**宽度**把窗口位置解成"亮区停在容器的固定位置"，唱到哪就滚到哪。
///
/// 为什么不用轮转：轮转是循环移位，已唱段在窗口里会落在接缝两侧、变成一段跨界的弧，而裁剪矩形只能表达一段连续区间，
/// 于是要么擦亮错位要么漏掉一段。跟随式只向后丢弃已经唱过的字、永不回头，已唱段因此在窗口里**始终是前缀**，
/// 擦亮就是"从字形起点裁到已唱位置"。
///
/// 为什么位置要按宽度求解：按"窗口内还能放下几个字"求解会形成反馈——能放下的字数取决于窗口位置，而窗口位置又取决于这个字数，
/// 两者互相追赶，混排内容（窄的拉丁字与宽的汉字）上表现为窗口来回跳（歌词越长、宽度差异越多越明显）。
/// 按宽度求解时位置只取决于"亮区已经有多宽"，因此**对亮区单调**：唱到哪就滚到哪，永远不会回退。
/// Follow marquee: solves the window position by **width** so the reveal rests at a fixed spot in the container, which makes the line
/// scroll to wherever the singing has got to.
///
/// Why not rotation: rotation is a cyclic shift, which turns the sung run into an arc that crosses the seam, and a clip rectangle can
/// only express one contiguous span — the reveal would either sit on the wrong characters or drop a piece. The follow mode only ever
/// discards characters that are already sung and never wraps, so the sung run stays a **prefix** of the window and the reveal is simply
/// "clip from the first glyph to the sung position".
///
/// Why the position is solved by width: solving it from "how many characters still fit" is a feedback loop — that count depends on the
/// window position and the window position depends on the count, so the two chase each other and the window jumps back and forth on
/// mixed content, which is worse the longer the line and the more its glyph widths differ. Solving by width depends only on how wide the
/// sung run already is, so the position is **monotone in the reveal**: the line scrolls to wherever the singing has got to and never
/// moves backwards.
/// </summary>
public static class MarqueeFollowPolicy
{
    /// <summary>擦亮边界右侧保留的比例：亮区因此总是停在容器的这个位置，接下来要唱的字一直在右边留出可见宽度。 / Share of the visible width kept clear at the right edge, so the reveal always rests at that position and the upcoming characters stay visible.</summary>
    public const double FollowMarginRatio = 0.2;

    /// <summary>亮区停在容器的哪个位置，等于 1 − <see cref="FollowMarginRatio"/>。/ Where the reveal rests inside the container, which is one minus <see cref="FollowMarginRatio"/>.</summary>
    public const double RevealEdgeRatio = 1 - FollowMarginRatio;

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

    /// <summary>
    /// 已唱段需要从原文里丢弃多少宽度：亮区因此停在容器的 <see cref="RevealEdgeRatio"/> 处，唱到哪就滚到哪；
    /// 亮区还没到那个位置时给出 0（窗口停在原文开头）。
    /// How much width has to be dropped from the content's head so that the reveal rests at <see cref="RevealEdgeRatio"/> of the
    /// container; zero while the reveal has not reached that position yet, which leaves the window at the content's head.
    /// </summary>
    /// <param name="sungWidth">已唱段的宽度（DIP）。/ Width of the sung run in DIP.</param>
    /// <param name="availableWidth">容器宽度（DIP）。/ Container width in DIP.</param>
    public static double ResolveDroppedWidth(double sungWidth, double availableWidth)
    {
        if (!double.IsFinite(sungWidth) || !double.IsFinite(availableWidth) || availableWidth <= 0)
            return 0;

        return Math.Max(0, sungWidth - RevealEdgeRatio * availableWidth);
    }

    /// <summary>
    /// 前缀宽度表里某个（可含小数的）字符位置对应的宽度，在相邻两个前缀之间线性插值；位置越界时夹到已测范围内。
    /// 插值让"亮区已经有多宽"与窗口位置都连续变化，不会在字与字之间跳。
    /// Width at a possibly fractional character position in a prefix-width table, interpolated between two neighbouring prefixes and
    /// clamped into the measured range. The interpolation keeps both the sung width and the window position continuous instead of
    /// jumping between characters.
    /// </summary>
    /// <param name="prefixWidths">前缀宽度表，第 i 项是前 i 个字符的宽度。/ Prefix widths, whose i-th entry is the width of the first i characters.</param>
    /// <param name="measuredCharacters">表中已经测过的字符数（不小于 1）。/ Characters already measured in the table, at least one.</param>
    /// <param name="position">字符位置（可含小数）。/ Character position, possibly fractional.</param>
    public static double ResolveWidthAt(double[]? prefixWidths, int measuredCharacters, double position)
    {
        if (prefixWidths is null || prefixWidths.Length == 0 || !double.IsFinite(position))
            return 0;

        var last = Math.Clamp(measuredCharacters, 0, prefixWidths.Length - 1);
        if (last <= 0)
            return prefixWidths[0];

        return Interpolate(prefixWidths, last, Math.Clamp(position, 0, last));
    }

    /// <summary>
    /// <see cref="ResolveWidthAt"/> 的反函数：给出某段宽度对应的字符位置，同样在相邻两个前缀之间插值。
    /// 表按宽度单调递增，因此用二分查找；宽度越界时夹到 [0, 已测字符数]。
    /// Inverse of <see cref="ResolveWidthAt"/>: the character position whose prefix width is the given width, likewise interpolated
    /// between two neighbouring prefixes. The table grows monotonically with width, so a binary search finds it; out-of-range widths are
    /// clamped into [0, measured characters].
    /// </summary>
    /// <param name="prefixWidths">前缀宽度表。/ Prefix-width table.</param>
    /// <param name="measuredCharacters">表中已经测过的字符数（不小于 1）。/ Characters already measured in the table, at least one.</param>
    /// <param name="width">目标宽度（DIP）。/ Target width in DIP.</param>
    public static double ResolvePositionAtWidth(double[]? prefixWidths, int measuredCharacters, double width)
    {
        if (prefixWidths is null || prefixWidths.Length == 0 || !double.IsFinite(width))
            return 0;

        var last = Math.Clamp(measuredCharacters, 0, prefixWidths.Length - 1);
        if (last <= 0 || width <= prefixWidths[0])
            return 0;

        if (width >= prefixWidths[last])
            return last;

        var low = 0;
        var high = last;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (prefixWidths[middle] <= width)
                low = middle;
            else
                high = middle - 1;
        }

        var next = Math.Min(low + 1, last);
        return next == low ? low : InterpolatePosition(prefixWidths, low, next, width);
    }

    /// <summary>呈现用的已唱宽度每帧最多追赶多少（相对字号的倍数）：一次上报带来的台阶因此被摊到几帧里，而不是整块文字跳一下。 / How much the presented sung width may catch up per frame, as a multiple of the font size, so a step from one position report is spread over a few frames instead of jumping the whole line.</summary>
    public const double MaximumRevealAdvanceEm = 1.6;

    /// <summary>
    /// 按擦亮进度换算原文中已唱到的位置（字符，可含小数）。/ Converts the reveal progress into the sung position in the content, in possibly fractional characters.
    /// </summary>
    /// <param name="progress">擦亮进度（0–1）。/ Reveal progress, 0–1.</param>
    /// <param name="contentLength">原文字符数。/ Content length in characters.</param>
    public static double ResolveSungPosition(double progress, int contentLength)
    {
        if (contentLength <= 0 || !double.IsFinite(progress))
            return 0;

        return Math.Clamp(progress, 0, 1) * contentLength;
    }

    /// <summary>
    /// 呈现层使用的已唱宽度：**只前进**，而且每帧最多追赶 <paramref name="maximumAdvance"/>。
    ///
    /// 播放位置来自播放器上报的时间轴（`MediaSnapshot.Position` + `LastUpdatedTime`），很多播放器按整秒上报，
    /// 于是我们外推出来的位置会每秒被"修正"回退一次；如果窗口位置直接跟着它走，整块文字就会跟着往回跳。
    /// 呈现层因此只承认前进：小的回退等播放位置自己追上，真正的跳转（切换行、跳转播放）由行内容变化触发的重启处理。
    /// The presented sung width: it only ever moves forward and catches up by at most <paramref name="maximumAdvance"/> per frame.
    ///
    /// The playback position comes from the player's reported timeline (`MediaSnapshot.Position` plus `LastUpdatedTime`), and many players
    /// report whole seconds, so the position we extrapolate is corrected backwards once per second. Driving the window straight from it
    /// would drag the whole line backwards. The presentation therefore only accepts forward motion: a small regression simply waits for the
    /// playback position to catch up, and a real jump is handled by the restart that a line change triggers.
    /// </summary>
    /// <param name="previousWidth">上一帧呈现的宽度（DIP）。/ Width presented last frame, in DIP.</param>
    /// <param name="targetWidth">本次读到的宽度（DIP）。/ Width read this time, in DIP.</param>
    /// <param name="maximumAdvance">本帧允许的最大追赶量（DIP）。/ Largest catch-up allowed this frame, in DIP.</param>
    public static double ResolveRevealWidth(double previousWidth, double targetWidth, double maximumAdvance)
    {
        var previous = double.IsFinite(previousWidth) && previousWidth > 0 ? previousWidth : 0;
        if (!double.IsFinite(targetWidth) || targetWidth <= previous)
            return previous;

        var advance = targetWidth - previous;
        var cap = double.IsFinite(maximumAdvance) && maximumAdvance > 0 ? maximumAdvance : advance;
        return previous + Math.Min(advance, cap);
    }

    private static double Interpolate(double[] prefixWidths, int last, double position)
    {
        var index = (int)Math.Floor(position);
        var next = Math.Min(index + 1, last);
        return next == index
            ? prefixWidths[index]
            : prefixWidths[index] + (position - index) * (prefixWidths[next] - prefixWidths[index]);
    }

    private static double InterpolatePosition(double[] prefixWidths, int low, int next, double width)
    {
        var span = prefixWidths[next] - prefixWidths[low];
        return span <= 0 ? low : low + (width - prefixWidths[low]) / span;
    }
}
