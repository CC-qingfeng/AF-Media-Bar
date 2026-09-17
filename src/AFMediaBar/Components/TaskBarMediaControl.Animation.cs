using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的动效辅助实现。/ Motion helpers for the taskbar media control.
/// </summary>
public partial class TaskBarMediaControl
{
    private MotionProfile CurrentMotion => MotionPolicy.ResolveCurrent();

    private static PowerEase CreateEaseOut() => new() { Power = 3, EasingMode = EasingMode.EaseOut };

    private static PowerEase CreateEaseInOut() => new() { Power = 3, EasingMode = EasingMode.EaseInOut };

    /// <summary>
    /// 注册一个跑马灯元素，并把它的小数位移变换挂到渲染上。歌词的高亮层作为第二层传入，两层共用同一个变换实例，
    /// 因此小数位移同时作用在底色层与高亮层上，亮区与字形始终保持对齐。
    /// Registers one marquee element and attaches its fractional-offset transform to the render. The lyric highlight layer is passed as
    /// the second layer so both share one transform instance: the fractional offset then applies to both layers and the reveal stays
    /// aligned with the glyphs.
    /// </summary>
    /// <param name="element">被推进的文本元素。/ The advanced text element.</param>
    /// <param name="mirroredLayer">需要一起位移的镜像层，没有则省略。/ Mirrored layer that moves along, omitted when there is none.</param>
    private void AddMarqueeText(TextBlock element, TextBlock? mirroredLayer = null)
    {
        var state = new MarqueeTextState(element);
        element.RenderTransform = state.Transform;
        if (mirroredLayer is not null)
        {
            // 镜像层用自己的变换实例，但每帧写同一个位移：亮区因此与字形一起移动，且不依赖共享 Freezable 的行为。
            // The mirrored layer gets its own transform instance written with the same offset every frame, so the reveal moves with the
            // glyphs without depending on shared-Freezable behaviour.
            state.MirroredTransform = new TranslateTransform();
            mirroredLayer.RenderTransform = state.MirroredTransform;
        }

        _marqueeTexts.Add(state);
    }

    /// <summary>
    /// 立即应用跑马灯与文字宽度。这里 MUST 同步执行而不是投递到 Loaded 优先级：宿主几何刚写完容器宽度，
    /// 紧接着就是"这段文字放不放得下"的答案，中间插入任何一次几何更新都会让这一步落在旧宽度上。
    /// Applies the marquee and the text widths immediately. This must run synchronously instead of being posted at Loaded priority: the
    /// host geometry has just written the container widths and the answer to "does this text fit" follows right after; letting a geometry
    /// update slip in between leaves this step working from the previous width.
    /// </summary>
    /// <param name="availableWidth">文字区的可用宽度（DIP）。/ Available width of the text region in DIP.</param>
    internal void ApplyMarqueeLayout(double availableWidth)
    {
        // 判据是"文字真的超出了可用宽度"，而不是长度模式：跟随内容模式下媒体栏被任务栏安全上限夹住时，
        // 文字同样会超出，此时也必须能推进看全，否则用户只能看到被截断的标题。
        // The criterion is the text actually overflowing its available width rather than the length mode: in follow-content mode the bar
        // is clamped by the taskbar's safe maximum, the text overflows there too, and it has to stay readable as well.
        var enabled = _currentMode == WindowMode.Taskbar &&
                      !_isVertical &&
                      _isConnected &&
                      CurrentMotion.UseContinuousMotion;
        var anyAdvancing = false;
        foreach (var state in _marqueeTexts)
            anyAdvancing |= ConfigureMarqueeText(state, enabled, availableWidth);

        if (!anyAdvancing)
        {
            _marqueeTimer.Stop();
            return;
        }

        if (!_marqueeTimer.IsEnabled)
            _marqueeTimer.Start();
    }

    /// <summary>
    /// 按当前内容与可用宽度决定一个文本元素用哪种方式推进，并把该写回的属性写回。
    ///
    /// 正在逐字擦亮的歌词行用跟随式（只丢弃已唱过的字，已唱段始终是窗口前缀，擦亮因此永远精确）；
    /// 其余元素用轮转式（没有"唱到哪"的概念，循环滚动才能反复读到）。
    /// Decides how one text element advances for its current content and available width, and writes back the properties that belong to
    /// that decision.
    ///
    /// A lyric line that is being revealed syllable by syllable uses the follow mode, which only discards characters that are already
    /// sung and therefore keeps the sung run a prefix of the window, so the reveal stays exact. Everything else uses rotation, which
    /// loops because it has no notion of "how far the singing has reached".
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    /// <param name="enabled">当前是否允许推进。/ Whether advancing is currently allowed.</param>
    /// <param name="availableWidth">可用宽度（DIP）。/ Available width in DIP.</param>
    /// <returns>该元素当前是否正在推进。/ Whether that element is currently advancing.</returns>
    private bool ConfigureMarqueeText(MarqueeTextState state, bool enabled, double availableWidth)
    {
        var element = state.Element;
        var available = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        // 应用写入的才是原文：只有当前文本不是我们上一次写进去的窗口时才重新捕获，否则会把窗口误当原文，
        // 于是每推进一轮内容就被缩掉一截。
        // Only what the application wrote counts as the content: it is re-captured when the current text is not the window written last
        // time, otherwise the window would be mistaken for the content and the text would shrink once per round.
        if (!string.Equals(element.Text, state.Window, StringComparison.Ordinal))
            state.Base = element.Text ?? string.Empty;

        if (!state.Advancing)
            state.Alignment = ResolveConfiguredAlignment(element);

        var measured = string.IsNullOrEmpty(state.Base) ? 0 : MeasureTextWidthExact(state.Base, element);
        var overflow = enabled && available > 0
            ? TaskbarExperiencePolicy.CalculateMarqueeOverflow(measured, available)
            : 0;
        var following = overflow > 1 && state.Base.Length > 0 && IsFollowMarqueeElement(element) && _lyricHighlightActive;
        var advancing = overflow > 1 && state.Base.Length > 0;
        var key = $"{advancing}|{following}|{available:0.##}|{state.Base}";
        if (!string.Equals(state.Key, key, StringComparison.Ordinal))
        {
            // 内容、宽度、方式或允许状态变化时从头开始，并重新走一遍起读停留。
            // A content, width, mode, or permission change restarts from the head and repeats the lead-in pause.
            state.Key = key;
            state.Position = 0;
            state.WindowStart = 0;
            state.Phase = 0;
            state.LeadIn = MarqueeTiming.LeadInCharacters;
        }

        state.AvailableWidth = available;
        state.Following = following;
        if (!advancing)
        {
            state.Advancing = false;
            state.Position = 0;
            state.WindowStart = 0;
            state.Phase = 0;
            state.Window = state.Base;
            state.WindowPrefixWidths = null;
            state.VisibleCharacters = 0;
            state.SungPosition = 0;
            state.SungWidthDip = 0;
            state.WindowWidthDip = 0;
            if (!string.Equals(element.Text, state.Base, StringComparison.Ordinal))
                element.Text = state.Base;
            // 放得下时保持用户设置的裁剪提示；放不下但当前不允许推进时也一样，省略号至少说明后面还有内容。
            // When it fits, the configured trimming hint stays; the same applies while advancing is not allowed, where the ellipsis at
            // least says that more text follows.
            if (element.TextTrimming != TextTrimming.CharacterEllipsis)
                element.TextTrimming = TextTrimming.CharacterEllipsis;
            if (Math.Abs(element.Width - available) > 0.01)
                element.Width = available;
            if (element.TextAlignment != state.Alignment)
                element.TextAlignment = state.Alignment;
            ApplyMarqueeOffset(state);
            return false;
        }

        state.Advancing = true;
        state.WindowLength = MarqueeRotationPolicy.ResolveWindowLength(state.Base.Length);
        UpdateMarqueeWindow(state);
        if (following)
        {
            // 立即按当前亮区算一次：擦亮帧（33 ms）可能先于第一帧推进（16 ms）到来，先算一次可以让头一帧就亮到位。
            // Solve once right away from the current reveal: the reveal frame (33 ms) can arrive before the first advance frame
            // (16 ms), and doing it here keeps the very first frame revealed where it belongs.
            state.SungPosition = MarqueeFollowPolicy.ResolveSungPosition(ResolveCurrentLyricProgress() ?? 0, state.Base.Length);
            state.SungWidthDip = MarqueeFollowPolicy.ResolveSungWidth(
                state.WindowPrefixWidths,
                state.SungPosition - state.WindowStart);
        }

        return true;
    }

    /// <summary>该元素是否使用跟随式推进（正在逐字擦亮的歌词行）。 / Whether the element uses the follow mode, which means the lyric line currently being revealed.</summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private bool IsFollowMarqueeElement(TextBlock element) => ReferenceEquals(element, SongLyrics);

    /// <summary>
    /// 按当前位置改写窗口：跟随式按亮区求解起点，轮转式按位置轮转字符串。窗口字符串只在整数位置跨过时才变化，
    /// 小数部分由 <see cref="ApplyMarqueeOffset"/> 写进渲染变换。
    /// Rewrites the window for the current position: the follow mode solves its start from the reveal and the rotation mode rotates the
    /// string. The window string only changes when the integer position crosses; the fraction is written into the render transform by
    /// <see cref="ApplyMarqueeOffset"/>.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void UpdateMarqueeWindow(MarqueeTextState state)
    {
        state.WindowStart = state.Following
            ? MarqueeFollowPolicy.SnapStart(
                state.Base,
                (int)Math.Floor(state.Position),
                MarqueeFollowPolicy.ResolveMaximumStart(state.Base.Length, state.VisibleCharacters))
            : MarqueeRotationPolicy.ResolveWindowStart(state.Base, (int)Math.Floor(state.Position));
        // 起点可以落在理想位置**之后**（对齐到文本元素边界时），因此小数位移是有符号的：起点在理想位置之后要向右补回一点，
        // 否则那一相位会跳一个字。
        // The start may land after the ideal position when it snapped to a text-element boundary, so the fractional offset is signed: a
        // start past the ideal position has to be compensated towards the right, or that phase would jump by one character.
        state.Phase = state.Position - state.WindowStart;
        WriteMarqueeWindow(state);
    }

    /// <summary>
    /// 重新计算窗口的度量：每个前缀的精确宽度、完整可见的字符数。只在窗口字符串变化时算一次而不是每帧算一次，
    /// 因为窗口只在整数位置跨过时变化，而擦亮每帧都要读这几个值。
    /// Recomputes the window's metrics: the exact width of every prefix and how many characters are fully visible. This runs once per
    /// window change rather than once per frame, because the window only changes when the integer position crosses while the reveal
    /// reads these values every frame.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void UpdateMarqueeWindowMetrics(MarqueeTextState state)
    {
        if (!state.Following)
        {
            state.WindowPrefixWidths = null;
            state.VisibleCharacters = 0;
            state.SungWidthDip = 0;
            state.WindowWidthDip = 0;
            return;
        }

        var content = state.Base;
        var prefixWidths = new double[Math.Max(1, content.Length - state.WindowStart + 1)];
        for (var index = 1; index < prefixWidths.Length; index++)
        {
            prefixWidths[index] = MeasureTextWidthExact(content[state.WindowStart..(state.WindowStart + index)], state.Element);
        }

        state.WindowPrefixWidths = prefixWidths;
        state.VisibleCharacters = MarqueeFollowPolicy.ResolveVisibleCharacterCount(prefixWidths, state.AvailableWidth);
        state.WindowWidthDip = prefixWidths[^1];
    }

    /// <summary>
    /// 把一个正在推进的元素写成当前窗口。窗口文字与容器等宽并硬裁：推进期间不需要省略号（字符会依次进入窗口），
    /// 硬裁也让逐字擦亮的裁剪边界与可见字形一一对应。
    /// Writes one advancing element as its current window. The window text is exactly as wide as its container and hard-cut: an ellipsis
    /// is pointless while characters keep entering the window, and a hard cut keeps the reveal clip aligned with the visible glyphs.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void WriteMarqueeWindow(MarqueeTextState state)
    {
        var element = state.Element;
        var window = state.Following
            ? MarqueeFollowPolicy.BuildWindow(state.Base, state.WindowStart)
            : MarqueeRotationPolicy.BuildWindow(state.Base, state.WindowStart);
        if (!string.Equals(state.Window, window, StringComparison.Ordinal))
        {
            state.Window = window;
            // 开头那个字符的步进宽度就是小数位移的换算基准：位置的小数部分代表"这个字已经移出多少"。
            // The head character's advance is what converts the fractional offset: the fraction is how much of it has already left.
            state.HeadCharacterWidth = ResolveHeadCharacterAdvance(window, element);
        }

        if (!string.Equals(element.Text, state.Window, StringComparison.Ordinal))
            element.Text = state.Window;

        if (element.TextTrimming != TextTrimming.None)
            element.TextTrimming = TextTrimming.None;

        if (Math.Abs(element.Width - state.AvailableWidth) > 0.01)
            element.Width = state.AvailableWidth;

        // 窗口比容器宽时，居中或右对齐会把开头推出容器，窗口就不再是"从头开始"；推进期间统一按左对齐渲染，
        // 停止推进时由 ApplyMarqueeLayout 按设置恢复用户的对齐方式。
        // When the window is wider than its container, centring or right alignment would push its head outside and the window would no
        // longer read as "starting from the beginning"; while advancing it is rendered left-aligned, and ApplyMarqueeLayout restores the
        // user's alignment from the settings once advancing stops.
        if (element.TextAlignment != TextAlignment.Left)
            element.TextAlignment = TextAlignment.Left;

        UpdateMarqueeWindowMetrics(state);
        ApplyMarqueeOffset(state);
    }

    /// <summary>
    /// 窗口开头那个字符的**步进宽度**：用"整窗宽度减去掉头之后的宽度"求得，因此它与下一个字之间的字距调整也算在内。
    /// 单独测一个字形得到的是孤立宽度，字距调整会让窗口轮转的那一刻差出不到一个像素——那正是"顿"的来源。
    /// Advance width of the window's head character: it is the window's width minus the width without its head, which therefore includes
    /// the kerning towards the next character. Measuring the glyph in isolation gives its standalone width, and the kerning difference
    /// makes the wrap moment land off by a fraction of a pixel, which is exactly what reads as a stutter.
    /// </summary>
    /// <param name="window">当前窗口文字。/ Current window text.</param>
    /// <param name="element">文本元素（取字号与字族）。/ Text element, for the font size and family.</param>
    private static double ResolveHeadCharacterAdvance(string window, TextBlock element)
    {
        if (string.IsNullOrEmpty(window))
            return 0;

        var head = MarqueeTextBoundary.FirstElement(window);
        if (head.Length >= window.Length)
            return MeasureTextWidthExact(window, element);

        var advance = MeasureTextWidthExact(window, element) - MeasureTextWidthExact(window[head.Length..], element);
        return advance > 0 ? advance : MeasureTextWidthExact(head, element);
    }

    /// <summary>
    /// 把窗口的小数位置写进渲染变换：窗口字符串只在整数位置跨过时改写，小数部分因此由连续的位移补上——
    /// 滚动是连续的，而不是一个字一个字地跳。歌词两层各有一个变换实例，每帧写同一个位移，亮区因此与字形一起移动。
    /// Writes the window's fractional position into the render transform: the window string is rewritten only when the integer position
    /// crosses, so the fraction is drawn as a continuous translation, which is what makes the scroll smooth instead of jumping one
    /// character at a time. Each lyric layer has its own transform instance, written with the same offset every frame, so the reveal moves
    /// with the glyphs.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void ApplyMarqueeOffset(MarqueeTextState state)
    {
        var offset = state.Advancing ? -state.Phase * state.HeadCharacterWidth : 0;
        if (!double.IsFinite(offset))
            offset = 0;

        WriteMarqueeOffset(state.Transform, offset);
        WriteMarqueeOffset(state.MirroredTransform, offset);
    }

    /// <summary>把一个变换的位移写成目标值；变换不存在或已经一致时不做任何事。/ Writes one transform's offset, doing nothing when there is no transform or it already matches.</summary>
    /// <param name="transform">目标变换。/ Target transform.</param>
    /// <param name="offset">位移（DIP）。/ Offset in DIP.</param>
    private static void WriteMarqueeOffset(TranslateTransform? transform, double offset)
    {
        if (transform is not null && Math.Abs(transform.X - offset) > 0.01)
            transform.X = offset;
    }

    /// <summary>把位置按窗口长度回绕到 [0, length)。/ Wraps a position into [0, length) by the window length.</summary>
    /// <param name="position">连续位置（字符）。/ Continuous position in characters.</param>
    /// <param name="length">窗口长度。/ Window length.</param>
    private static double WrapPosition(double position, int length)
    {
        if (length <= 0 || !double.IsFinite(position))
            return 0;

        var wrapped = position % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    /// <summary>
    /// 读取正在逐字擦亮的歌词行窗口：窗口内已唱段的裁剪宽度与窗口整体宽度。没有启用跟随时返回 false，
    /// 调用方按整行前缀换算擦亮。
    /// Reads the window of the lyric line that is being revealed: the clip width of the sung run inside it and the window's total
    /// width. Returns false when the follow mode is off, in which case the caller converts the reveal from the whole line instead.
    /// </summary>
    /// <param name="sungWidthDip">窗口内已唱段的宽度（DIP）。/ Width of the sung run inside the window, in DIP.</param>
    /// <param name="windowWidthDip">当前窗口的整体宽度（DIP），用于换算字形内缩。/ Total width of the current window in DIP, used to convert the glyph inset.</param>
    /// <param name="contentPosition">原文中已唱到的位置（字符，可含小数）。/ Sung position in the content, in possibly fractional characters.</param>
    /// <param name="contentLength">原文的字符数。/ Content length in characters.</param>
    internal bool TryGetFollowWindow(
        out double sungWidthDip,
        out double windowWidthDip,
        out double contentPosition,
        out int contentLength)
    {
        foreach (var state in _marqueeTexts)
        {
            if (!IsFollowMarqueeElement(state.Element) || !state.Following)
                continue;

            sungWidthDip = state.SungWidthDip;
            windowWidthDip = state.WindowWidthDip;
            contentPosition = state.SungPosition;
            contentLength = state.Base.Length;
            return true;
        }

        sungWidthDip = 0;
        windowWidthDip = 0;
        contentPosition = 0;
        contentLength = 0;
        return false;
    }

    /// <summary>
    /// 按帧推进每个正在推进的元素：起读停留期间保持原位，之后每帧前进
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> 个字符。跟随式每帧按亮区重新求解位置，因此唱得快时窗口跟得快。
    /// Advances every element by one frame: it stays put during the lead-in pause and then moves by
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> characters per frame. The follow mode re-solves its position from the reveal on every
    /// frame, so a fast line scrolls just as fast.
    /// </summary>
    private void AdvanceMarqueeStep()
    {
        var anyAdvancing = false;
        foreach (var state in _marqueeTexts)
        {
            if (!state.Advancing)
                continue;

            anyAdvancing = true;
            // 起读停留只属于轮转式：跟随式的位置由亮区决定，等一拍会让亮区先跑出窗口。
            // The lead-in pause belongs to the rotation mode only: a follow window's position is decided by the reveal, and waiting a beat
            // would let the reveal leave the window first.
            if (!state.Following && state.LeadIn > 0)
            {
                state.LeadIn = Math.Max(0, state.LeadIn - MarqueeTiming.CharactersPerFrame);
                continue;
            }

            if (state.Following)
            {
                AdvanceFollow(state);
                continue;
            }

            state.Position = WrapPosition(state.Position + MarqueeTiming.CharactersPerFrame, state.WindowLength);
            var windowStart = MarqueeRotationPolicy.ResolveWindowStart(state.Base, (int)Math.Floor(state.Position));
            state.Phase = state.Position - windowStart;
            if (windowStart != state.WindowStart)
            {
                // 整数位置跨过：改写窗口字符串（并重新测一次开头字符的宽度）。
                // The integer position crossed: rewrite the window string and re-measure the head character's width.
                state.WindowStart = windowStart;
                WriteMarqueeWindow(state);
                continue;
            }

            ApplyMarqueeOffset(state);
        }

        if (!anyAdvancing)
            _marqueeTimer.Stop();
    }

    /// <summary>
    /// 跟随式的一帧：位置由亮区求解，因此"唱到哪滚到哪"。位置每帧重解而不是每 220 毫秒挪一个字，
    /// 否则唱得快时窗口跟不上，亮区就会跑到窗口可见区之外（表现为高亮超出显示范围）。
    /// One follow frame: the position is solved from the reveal, so the line scrolls to wherever the singing has got to. It is solved on
    /// every frame instead of shifting one character per 220 ms, because otherwise a fast line outruns the window and the reveal leaves
    /// the visible region.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void AdvanceFollow(MarqueeTextState state)
    {
        var contentLength = state.Base.Length;
        var progress = ResolveCurrentLyricProgress();
        var sung = progress is null ? 0 : MarqueeFollowPolicy.ResolveSungPosition(progress.Value, contentLength);
        state.SungPosition = sung;
        state.Position = MarqueeFollowPolicy.ResolvePosition(sung, contentLength, state.VisibleCharacters);

        var windowStart = MarqueeFollowPolicy.SnapStart(
            state.Base,
            (int)Math.Floor(state.Position),
            MarqueeFollowPolicy.ResolveMaximumStart(contentLength, state.VisibleCharacters));
        state.Phase = state.Position - windowStart;
        if (windowStart != state.WindowStart)
        {
            state.WindowStart = windowStart;
            WriteMarqueeWindow(state);
        }

        state.SungWidthDip = MarqueeFollowPolicy.ResolveSungWidth(state.WindowPrefixWidths, sung - state.WindowStart);
        ApplyMarqueeOffset(state);
    }

    /// <summary>
    /// 停止推进并把每个元素还原成应用写入的原文、取消位移，同时恢复用户设置的对齐方式。悬停进入、切换模式与卸载都走这里，
    /// 因为"还原"必须与"停止计时器"是同一件事，否则屏幕上会留下一段被推进过的文字。
    /// Stops advancing, restores every element to the content the application wrote, clears the offset, and restores the configured
    /// alignment. Hover entry, mode switches, and unloading all go through here, because restoring and stopping the timer have to be one
    /// operation — otherwise an advanced window would be left on screen.
    /// </summary>
    private void StopMarqueeAnimations()
    {
        _marqueeTimer.Stop();
        foreach (var state in _marqueeTexts)
        {
            state.Key = string.Empty;
            state.Position = 0;
            state.WindowStart = 0;
            state.Phase = 0;
            state.LeadIn = MarqueeTiming.LeadInCharacters;
            state.Advancing = false;
            state.Following = false;
            state.Window = state.Base;
            state.WindowPrefixWidths = null;
            state.VisibleCharacters = 0;
            state.SungPosition = 0;
            state.SungWidthDip = 0;
            state.WindowWidthDip = 0;
            if (!string.Equals(state.Element.Text, state.Base, StringComparison.Ordinal))
                state.Element.Text = state.Base;
            if (state.Element.TextAlignment != state.Alignment)
                state.Element.TextAlignment = state.Alignment;
            ApplyMarqueeOffset(state);
        }
    }

    /// <summary>
    /// 该元素在设置里配置的对齐方式。推进期间渲染按左对齐，结束后必须回到设置里的值，
    /// 因此这里读设置而不是读元素（元素上那个值可能已经被推进覆盖）。
    /// The alignment configured in the settings for that element. Advancing renders left-aligned and has to return to the configured value
    /// afterwards, so this reads the settings rather than the element, whose own value may already have been overwritten.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private TextAlignment ResolveConfiguredAlignment(TextBlock element)
    {
        if (ReferenceEquals(element, SongLyrics) ||
            ReferenceEquals(element, SongLyricsHighlight) ||
            ReferenceEquals(element, SongLyricsSecondary))
        {
            return SettingsManager.Current.LyricsTextAlignment switch
            {
                LyricsTextAlignment.Left => TextAlignment.Left,
                LyricsTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center
            };
        }

        return SettingsManager.Current.TaskbarExperience.Normalize().MediaTextAlignment switch
        {
            TaskbarMediaTextAlignment.Center => TextAlignment.Center,
            TaskbarMediaTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    /// <summary>
    /// 一个文本元素的推进状态：应用写入的原文、我们最后写进去的窗口、连续位置与窗口度量缓存。
    /// Advance state of one text element: the content the application wrote, the window written last time, the continuous position, and
    /// the window's cached metrics.
    /// </summary>
    private sealed class MarqueeTextState(TextBlock element)
    {
        /// <summary>被推进的文本元素。/ The advanced text element.</summary>
        public TextBlock Element { get; } = element;

        /// <summary>小数位移用的渲染变换；歌词两层的镜像层另持一个实例，两个实例每帧写同一个值。 / Render transform carrying the fractional offset; a lyric line's mirrored layer has its own instance and both are written with the same value every frame.</summary>
        public TranslateTransform Transform { get; } = new();

        /// <summary>需要跟着一起位移的镜像层变换（歌词高亮层），没有则为 null。/ Transform of the mirrored layer that moves along, the lyric highlight layer, or null when there is none.</summary>
        public TranslateTransform? MirroredTransform { get; set; }

        /// <summary>应用写入的原文。/ Content written by the application.</summary>
        public string Base { get; set; } = string.Empty;

        /// <summary>我们最后写进去的窗口文字。/ Window text written last time.</summary>
        public string Window { get; set; } = string.Empty;

        /// <summary>决定是否需要重启推进的一致性键。/ Consistency key deciding whether advancing has to restart.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>连续位置（字符）：轮转式按窗口长度回绕，跟随式是原文里的起点。/ Continuous position in characters: wrapped by the window length in the rotation mode and the content start in the follow mode.</summary>
        public double Position { get; set; }

        /// <summary>当前窗口字符串对应的整数位置。/ Integer position behind the current window string.</summary>
        public int WindowStart { get; set; }

        /// <summary>位置的小数部分，由渲染变换承担。/ Fractional part of the position, carried by the render transform.</summary>
        public double Phase { get; set; }

        /// <summary>窗口开头字符的宽度（DIP），用于把小数位置换算成位移。/ Width of the window's head character in DIP, which converts the fractional position into a translation.</summary>
        public double HeadCharacterWidth { get; set; }

        /// <summary>轮转式窗口的字符数（原文加间隔）。/ Rotation window length in characters: content plus separator.</summary>
        public int WindowLength { get; set; }

        /// <summary>起读停留剩余的字符数。/ Remaining lead-in in characters.</summary>
        public double LeadIn { get; set; } = MarqueeTiming.LeadInCharacters;

        /// <summary>该元素当前是否正在推进。/ Whether that element is currently advancing.</summary>
        public bool Advancing { get; set; }

        /// <summary>该元素是否使用跟随式。/ Whether that element uses the follow mode.</summary>
        public bool Following { get; set; }

        /// <summary>当前可用宽度（DIP）。/ Current available width in DIP.</summary>
        public double AvailableWidth { get; set; }

        /// <summary>设置里配置的对齐方式。/ Alignment configured in the settings.</summary>
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;

        /// <summary>跟随式窗口的前缀宽度表；轮转式为 null。/ Prefix widths of a follow window; null in the rotation mode.</summary>
        public double[]? WindowPrefixWidths { get; set; }

        /// <summary>跟随式窗口内完整可见的字符数。/ Characters fully visible inside a follow window.</summary>
        public int VisibleCharacters { get; set; }

        /// <summary>跟随式里原文中已唱到的位置（字符，可含小数）。/ Sung position in the content for the follow mode, in possibly fractional characters.</summary>
        public double SungPosition { get; set; }

        /// <summary>跟随式里窗口内已唱段的裁剪宽度（DIP）。/ Clip width of the sung run inside the window, in DIP.</summary>
        public double SungWidthDip { get; set; }

        /// <summary>跟随式里窗口的整体宽度（DIP）。/ Total width of the follow window in DIP.</summary>
        public double WindowWidthDip { get; set; }
    }

    private static double MeasureTextWidth(string text, TextBlock source) =>
        MeasureTextWidthExact(text, source) + 4;

    /// <summary>
    /// 测量文字的自然宽度，不含跑马灯为折返预留的 4 DIP。
    /// Measures the natural text width without the 4 DIP the marquee reserves for its turn-around.
    ///
    /// 逐字擦亮需要这个不带补偿的宽度：裁剪边界按字形实际占用的宽度换算，多出来的 4 DIP 会让擦亮在行尾提前结束。
    /// Syllable highlighting needs the width without that compensation: the clip edge converts glyph width, and the extra 4 DIP
    /// would make the reveal finish before the end of the line.
    /// </summary>
    private static double MeasureTextWidthExact(string text, TextBlock source)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var pixelsPerDip = VisualTreeHelper.GetDpi(source).PixelsPerDip;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
            source.FontSize,
            Brushes.Transparent,
            pixelsPerDip)
        {
            Trimming = TextTrimming.None
        };
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// 入场动画：标题/艺术家变化时触发淡入和左滑效果。
    /// Entrance animation: fade-in and left-slide effect when title/artist changes.
    /// </summary>
    private void AnimateEntrance()
    {
        try
        {
            var motion = CurrentMotion;
            if (!motion.UseTransitions)
            {
                SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
                SongInfoStackPanel.Opacity = _isTaskbarHoverVisible ? TaskbarCoveredOpacity : 1;
                if (SongInfoStackPanel.RenderTransform is TranslateTransform instantTransform)
                {
                    instantTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    instantTransform.X = 0;
                }
                return;
            }

            var duration = motion.StandardDuration;
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = _isTaskbarHoverVisible ? TaskbarCoveredOpacity : 1.0,
                Duration = duration,
                EasingFunction = CreateEaseOut()
            };
            var translateAnimation = new DoubleAnimation
            {
                From = -6,
                To = 0,
                Duration = duration,
                EasingFunction = CreateEaseOut()
            };

            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            var translateTransform = new TranslateTransform();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool CanUseTaskbarComponentHover() =>
        _isConnected && _currentMode == WindowMode.Taskbar && !_isVertical;

    private bool CanShowDirectFullPanelHandle()
    {
        var experience = SettingsManager.Current.TaskbarExperience;
        return CanUseTaskbarComponentHover() &&
               experience.FullPanelEntryVisible &&
               !experience.HoverLayerEnabled &&
               experience.FullLayerEnabled;
    }

    /// <summary>直接模糊并淡化原文字区域，悬停按钮作为独立兄弟元素保持清晰。</summary>
    private void AnimateSongInfoCovered(bool isCovered, bool immediate = false)
    {
        if (isCovered && !CanUseTaskbarComponentHover())
            return;

        var motion = CurrentMotion;
        BlurEffect? blur = null;
        var needsBlur = motion.UseDecorativeEffects &&
                        (isCovered || SongInfoStackPanel.Effect is BlurEffect);
        if (needsBlur)
        {
            if (SongInfoStackPanel.Effect is not BlurEffect currentBlur || currentBlur.IsFrozen)
            {
                currentBlur = new BlurEffect
                {
                    Radius = 0,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
                SongInfoStackPanel.Effect = currentBlur;
            }

            blur = currentBlur;
        }
        else if (SongInfoStackPanel.Effect is BlurEffect existingBlur)
        {
            // Effect 节点只要存在，文字就会一直被渲染到中间表面并失去字形保真度，
            // 因此不透明状态必须真正清空它，而不是留下一个半径为 0 的模糊。
            // While the effect node exists the text keeps rendering through an intermediate surface and loses glyph
            // fidelity, so the un-covered state must clear it instead of leaving a zero-radius blur behind.
            existingBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.Effect = null;
        }

        var targetRadius = motion.UseDecorativeEffects && isCovered ? TaskbarCoveredBlurRadius : 0;
        var targetOpacity = isCovered ? TaskbarCoveredOpacity : 1;
        if (immediate || !motion.UseTransitions)
        {
            blur?.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
            if (targetRadius > 0 && blur is not null)
                blur.Radius = targetRadius;
            else
                SongInfoStackPanel.Effect = null;
            SongInfoStackPanel.Opacity = targetOpacity;
            return;
        }

        var duration = isCovered ? motion.StandardDuration : motion.FastDuration;
        var easingMode = isCovered ? EasingMode.EaseOut : EasingMode.EaseInOut;
        if (blur is not null)
        {
            var radiusAnimation = new DoubleAnimation
            {
                To = targetRadius,
                Duration = duration,
                EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
            };
            if (targetRadius <= 0)
            {
                // 恢复动画结束后立即移除 Effect 节点，让文字回到直接合成的清晰状态。
                // Remove the effect node when the un-cover animation ends so the text composites directly again.
                radiusAnimation.Completed += (_, _) =>
                {
                    // 快速重入时新的覆盖状态可能已经换上另一个模糊，只在仍由本次恢复持有该节点时才清空。
                    // A quick re-entry can already have installed another blur, so clear the node only while this
                    // un-cover still owns it.
                    if (!ReferenceEquals(SongInfoStackPanel.Effect, blur))
                        return;

                    blur.BeginAnimation(BlurEffect.RadiusProperty, null);
                    SongInfoStackPanel.Effect = null;
                };
            }

            blur.BeginAnimation(BlurEffect.RadiusProperty, radiusAnimation, HandoffBehavior.SnapshotAndReplace);
        }
        SongInfoStackPanel.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = targetOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateDirectFullPanelHandle(bool isVisible, bool immediate = false)
    {
        var targetOpacity = isVisible && CanShowDirectFullPanelHandle() ? 1 : 0;
        var motion = CurrentMotion;
        if (immediate || !motion.UseTransitions)
        {
            TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, null);
            TaskbarDirectFullPanelHandle.Opacity = targetOpacity;
            return;
        }

        TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = targetOpacity,
            Duration = motion.FastDuration,
            EasingFunction = new CubicEase
            {
                EasingMode = targetOpacity > 0 ? EasingMode.EaseOut : EasingMode.EaseInOut
            }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>复用原 TaskBarMediaControl 的 hover 色彩和节奏，但将效果限制在单个组件。</summary>
    private void AnimateComponentHover(Border surface, bool isHovered)
    {
        if (isHovered && !CanUseTaskbarComponentHover())
            return;

        var isDark = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;
        var backgroundColor = isHovered
            ? isDark ? Color.FromArgb(197, 255, 255, 255) : Colors.White
            : Colors.Transparent;
        var backgroundOpacity = isHovered ? isDark ? 0.075 : 0.6 : 0;
        var borderColor = isHovered ? Color.FromArgb(93, 255, 255, 255) : Colors.Transparent;
        var borderOpacity = isHovered ? isDark ? 0.25 : 1 : 0;
        var easingMode = isHovered ? EasingMode.EaseOut : EasingMode.EaseInOut;

        if (surface.Background is not SolidColorBrush background || background.IsFrozen)
        {
            background = new SolidColorBrush(Colors.Transparent);
            surface.Background = background;
        }
        if (surface.BorderBrush is not SolidColorBrush border || border.IsFrozen)
        {
            border = new SolidColorBrush(Colors.Transparent);
            surface.BorderBrush = border;
        }

        var motion = CurrentMotion;
        if (!motion.UseTransitions)
        {
            background.BeginAnimation(SolidColorBrush.ColorProperty, null);
            background.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            border.BeginAnimation(SolidColorBrush.ColorProperty, null);
            border.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            background.Color = backgroundColor;
            background.Opacity = backgroundOpacity;
            border.Color = borderColor;
            border.Opacity = borderOpacity;
            return;
        }

        var duration = motion.StandardDuration;
        background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = backgroundColor,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        background.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = backgroundOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        border.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = borderColor,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        border.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = borderOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
    }

    private void SongImageBorder_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateComponentHover(SongImageHoverOverlay, true);

    private void SongImageBorder_MouseLeave(object sender, MouseEventArgs e) =>
        AnimateComponentHover(SongImageHoverOverlay, false);

    private void TaskbarSpectrumHoverSurface_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateComponentHover(TaskbarSpectrumHoverSurface, true);

    private void TaskbarSpectrumHoverSurface_MouseLeave(object sender, MouseEventArgs e) =>
        AnimateComponentHover(TaskbarSpectrumHoverSurface, false);

    private void SongInfoStackPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!CanUseTaskbarComponentHover())
            return;

        StopMarqueeAnimations();
        AnimateComponentHover(SongInfoHoverOverlay, true);
        AnimateDirectFullPanelHandle(true);
        _hoverCloseTimer.Stop();
        _hoverOpenTimer.Stop();
        if (SettingsManager.Current.TaskbarExperience.HoverLayerEnabled)
            _hoverOpenTimer.Start();
    }

    private void SongInfoStackPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverOpenTimer.Stop();
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        if (!_isTaskbarHoverVisible)
        {
            if (!TaskbarDirectFullPanelHandle.IsMouseOver)
            {
                AnimateComponentHover(SongInfoHoverOverlay, false);
                AnimateDirectFullPanelHandle(false);
            }
            return;
        }
        _hoverCloseTimer.Stop();
        _hoverCloseTimer.Start();
    }

    private void TaskbarHoverLayer_MouseEnter(object sender, MouseEventArgs e)
    {
        _hoverCloseTimer.Stop();
        AnimateComponentHover(SongInfoHoverOverlay, true);
    }

    private void TaskbarHoverLayer_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverCloseTimer.Stop();
        _hoverCloseTimer.Start();
    }

    private void TaskbarDirectFullPanelHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!CanShowDirectFullPanelHandle())
            return;
        AnimateComponentHover(SongInfoHoverOverlay, true);
        AnimateDirectFullPanelHandle(true);
    }

    private void TaskbarDirectFullPanelHandle_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SongInfoStackPanel.IsMouseOver)
            return;
        AnimateComponentHover(SongInfoHoverOverlay, false);
        AnimateDirectFullPanelHandle(false);
    }

    private void ShowTaskbarHoverLayer()
    {
        if (!_isConnected || _currentMode != WindowMode.Taskbar || _isVertical ||
            !SettingsManager.Current.TaskbarExperience.HoverLayerEnabled ||
            (!SongInfoStackPanel.IsMouseOver && !HoverRevealHost.IsMouseOver))
            return;

        ApplyTaskbarExperienceSettings();
        _isTaskbarHoverVisible = true;
        AnimateComponentHover(SongInfoHoverOverlay, true);
        AnimateSongInfoCovered(true);
        HoverRevealHost.Visibility = Visibility.Visible;
        HoverRevealHost.IsHitTestVisible = true;
        var targetWidth = Math.Max(0, SongInfoStackPanel.Width);
        HoverRevealHost.Width = targetWidth;
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        var currentWidth = Math.Clamp(HoverRevealClip.Rect.Width, 0, targetWidth);
        HoverRevealClip.Rect = new Rect(0, 0, currentWidth, HoverRevealHost.Height);
        if (!CurrentMotion.UseTransitions)
        {
            HoverRevealClip.Rect = new Rect(0, 0, targetWidth, HoverRevealHost.Height);
            return;
        }

        var reveal = new RectAnimation
        {
            From = new Rect(0, 0, currentWidth, HoverRevealHost.Height),
            To = new Rect(0, 0, targetWidth, HoverRevealHost.Height),
            Duration = CurrentMotion.PanelDuration,
            EasingFunction = CreateEaseOut()
        };
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, reveal, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideTaskbarHoverLayer(bool immediate = false)
    {
        _hoverOpenTimer.Stop();
        _hoverCloseTimer.Stop();
        if (immediate || HoverRevealHost.Visibility != Visibility.Visible)
        {
            HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            HoverRevealHost.Width = 0;
            HoverRevealClip.Rect = new Rect(0, 0, 0, HoverRevealHost.Height);
            HoverRevealHost.Visibility = Visibility.Collapsed;
            HoverRevealHost.IsHitTestVisible = false;
            _isTaskbarHoverVisible = false;
            AnimateSongInfoCovered(false, immediate: true);
            var keepRegularHover = CanUseTaskbarComponentHover() &&
                                   (SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver);
            AnimateComponentHover(SongInfoHoverOverlay, keepRegularHover);
            AnimateDirectFullPanelHandle(keepRegularHover, immediate: true);
            return;
        }

        AnimateSongInfoCovered(false);

        if (!CurrentMotion.UseTransitions)
        {
            HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            HoverRevealHost.Width = 0;
            HoverRevealClip.Rect = new Rect(0, 0, 0, HoverRevealHost.Height);
            HoverRevealHost.Visibility = Visibility.Collapsed;
            HoverRevealHost.IsHitTestVisible = false;
            _isTaskbarHoverVisible = false;
            return;
        }

        var hide = new RectAnimation
        {
            From = HoverRevealClip.Rect,
            To = new Rect(0, 0, 0, HoverRevealHost.Height),
            Duration = CurrentMotion.ExitDuration,
            EasingFunction = CreateEaseInOut()
        };
        hide.Completed += (_, _) =>
        {
            HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            HoverRevealHost.Width = 0;
            HoverRevealClip.Rect = new Rect(0, 0, 0, HoverRevealHost.Height);
            HoverRevealHost.Visibility = Visibility.Collapsed;
            HoverRevealHost.IsHitTestVisible = false;
            _isTaskbarHoverVisible = false;
            if (!SongInfoStackPanel.IsMouseOver && !TaskbarDirectFullPanelHandle.IsMouseOver)
                AnimateComponentHover(SongInfoHoverOverlay, false);
        };
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, hide, HandoffBehavior.SnapshotAndReplace);
    }

}
