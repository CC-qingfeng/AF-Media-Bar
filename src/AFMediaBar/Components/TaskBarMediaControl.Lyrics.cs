using System;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的逐字擦亮：当前行随播放位置从左向右亮起。
/// Syllable highlighting for the taskbar media control: the active line lights up from left to right with playback.
///
/// 擦亮只写高亮层的裁剪矩形，不改文本、不改布局、不改尺寸请求，因此跑马灯宽度、自动尺寸指纹与字号缩放都不受影响；
/// 没有逐字时间轴的来源、行级歌词、暂停、断开、高对比度以及动效降级都会直接关掉这一层，外观与不启用逐字时完全一致。
/// The highlight only writes the clip rectangle of the highlight layer and never touches text, layout, or the size request, so
/// the marquee width, the auto-size fingerprint, and the font scaling stay unaffected. Sources without a syllable timeline,
/// line-level lyrics, a paused or disconnected session, high contrast, and reduced motion all switch the layer off, leaving
/// the appearance exactly as it is without syllable support.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>擦亮帧间隔：约 30 帧每秒，与歌词行内的时间精度相称。
    /// Highlight frame interval: about 30 frames per second, matching the timing precision inside a lyric line.</summary>
    private const int LyricHighlightFrameIntervalMilliseconds = 33;

    /// <summary>启用擦亮时底色层的不透明度：同一支前景色压暗，形成"已唱/未唱"的对比。
    /// Opacity of the base layer while highlighting is active: the same foreground dimmed, which contrasts sung and unsung text.</summary>
    private const double LyricHighlightBaseOpacity = 0.45;

    /// <summary>裁剪宽度小于该值时不显示高亮层，避免行首出现一条几乎没有宽度的杂线。
    /// Below this clip width the highlight layer stays hidden, which keeps a hair-width line from appearing at the line start.</summary>
    private const double LyricHighlightMinimumWidth = 0.5;

    /// <summary>裁剪矩形在容器高度不可用时的兜底高度。/ Fallback clip height used when the container height is unavailable.</summary>
    private const double LyricHighlightFallbackHeight = 19;

    private readonly DispatcherTimer _lyricHighlightTimer;
    private LyricLine? _currentLyricLine;
    private LyricLine? _measuredLyricLine;
    private double _measuredLyricFontSize = double.NaN;
    private double _measuredLyricAvailableWidth = double.NaN;
    private double _activeLyricTextWidth;
    private bool _lyricHighlightActive;

    /// <summary>
    /// 记录当前行，供逐字擦亮读取；换行时立即收起旧擦亮。
    /// Records the active line for syllable highlighting and hides the previous reveal immediately on a line change.
    /// </summary>
    /// <param name="line">当前行；位置早于第一行时为 null / Active line, null before the first line.</param>
    private void SetCurrentLyricLine(LyricLine? line)
    {
        if (ReferenceEquals(_currentLyricLine, line))
        {
            return;
        }

        _currentLyricLine = line;
        _measuredLyricLine = null;
        // 换行必须先收起：新行的第一帧到来之前不能让上一行的亮区留在屏幕上。
        // A line change has to hide the layer first, so the previous line's reveal cannot stay on screen before the first frame
        // of the new one arrives.
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 按当前状态开启或关闭擦亮；已经开启时本方法不做任何事，避免每个快照都重写一次外观。
    /// Enables or disables highlighting for the current state; while it is already on this method does nothing, so an incoming
    /// snapshot never rewrites the appearance.
    /// </summary>
    private void RefreshLyricHighlightPresentation()
    {
        if (!CanAnimateLyricHighlight())
        {
            StopLyricHighlight();
            return;
        }

        if (_lyricHighlightActive)
        {
            return;
        }

        _lyricHighlightActive = true;
        SongLyrics.Opacity = LyricHighlightBaseOpacity;
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        _lyricHighlightTimer.Start();
        // 擦亮开启意味着这一行改为跟随滚动，因此要重新应用一次跑马灯。
        // Turning the reveal on means this line starts following the reveal, so the marquee has to be applied again.
        ReapplyLyricMarquee();
    }

    /// <summary>
    /// 关闭擦亮并还原外观。
    /// Turns highlighting off and restores the appearance.
    /// </summary>
    private void StopLyricHighlight()
    {
        var wasActive = _lyricHighlightActive;
        _lyricHighlightTimer.Stop();
        _lyricHighlightActive = false;
        _measuredLyricLine = null;
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Collapsed;
        SongLyrics.Opacity = 1;
        // 擦亮关闭后这一行回到轮转式，因此只有真的从开启切到关闭时才需要重新应用一次。
        // Once the reveal is off this line goes back to rotation, so the marquee only has to be re-applied on a real on-to-off transition.
        if (wasActive)
            ReapplyLyricMarquee();
    }

    /// <summary>
    /// 判断当前是否具备逐字擦亮的条件。
    /// Decides whether syllable highlighting is currently possible.
    /// </summary>
    private bool CanAnimateLyricHighlight() =>
        _currentLyricLine is not null &&
        _snapshot.IsConnected &&
        _snapshot.IsPlaying &&
        SongLyricsPanel.Visibility == Visibility.Visible &&
        IsVisible &&
        !SystemParameters.HighContrast &&
        CurrentMotion.UseContinuousMotion;

    private void AdvanceLyricHighlight()
    {
        if (!CanAnimateLyricHighlight())
        {
            StopLyricHighlight();
            return;
        }

        var line = _currentLyricLine!;
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        var progress = LyricHighlightPolicy.ResolveProgress(line, position);
        if (progress is null)
        {
            StopLyricHighlight();
            return;
        }

        // 跟随式：这一行放不下，窗口正在跟着擦亮边界移动，因此窗口里的已唱段就是它的前缀，
        // 裁剪宽度直接取"窗口内前若干个字符的精确宽度"（含小数插值），不需要按整行比例折算。
        // Follow mode: this line does not fit and the window follows the reveal edge, so the sung run is a prefix of the window and the
        // clip width is simply the exact width of its leading characters, interpolated, instead of a fraction of the whole line.
        if (TryGetFollowWindow(out var sungWidth, out var windowWidth, out _, out _))
        {
            // 亮区最远只到容器右边缘：窗口比容器宽（尾部字更宽）时，按字符数换算出来的宽度可能超过可用宽度，
            // 那会让高亮画到显示范围之外。
            // The reveal never goes past the container's right edge: when the window is wider than its container (wider tail characters),
            // converting from a character count can exceed the available width, which would paint the highlight outside the visible area.
            var visibleWidth = double.IsFinite(SongLyrics.Width) ? Math.Min(sungWidth, SongLyrics.Width) : sungWidth;
            if (visibleWidth < LyricHighlightMinimumWidth)
            {
                SongLyricsHighlight.Visibility = Visibility.Collapsed;
                SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
                return;
            }

            var followLeft = ResolveFollowInset(SongLyrics.TextAlignment, windowWidth);
            SongLyricsHighlightClip.Rect = new Rect(followLeft, 0, visibleWidth, ResolveLyricClipHeight());
            SongLyricsHighlight.Visibility = Visibility.Visible;
            return;
        }

        EnsureLyricTextWidth(line);
        var width = LyricHighlightPolicy.ResolveClipWidth(progress.Value, _activeLyricTextWidth);
        if (width < LyricHighlightMinimumWidth)
        {
            SongLyricsHighlight.Visibility = Visibility.Collapsed;
            SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
            return;
        }

        // 裁剪矩形在文本自己的坐标系里，元素之外的位移（例如 SongInfoStackPanel 的入场动画）会带着两层一起走，
        // 亮区与字形始终对齐；跑马灯不再移动元素，它改写的是文字本身。
        // The clip rectangle lives in the text's own coordinate space, so a transform outside the element (such as the entrance
        // animation on SongInfoStackPanel) carries both layers along and the reveal stays aligned with the glyphs. The marquee no
        // longer moves the element: it rewrites the text itself.
        //
        // 居中或右对齐时字形本身有左内缩，裁剪必须从字形起点开始，否则短句的擦亮会整体偏左。
        // Centred or right-aligned text insets the glyph run, and the clip has to start where the glyphs do; otherwise the reveal
        // of a short line sits too far to the left.
        var left = ResolveLyricInset(SongLyrics.TextAlignment);
        SongLyricsHighlightClip.Rect = new Rect(left, 0, width, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 当前歌词行已经唱到的比例（0–1），供跑马灯判断"擦亮是否贴近右边界"。没有音节时间轴或位置不可解时返回 null。
    /// Reveal progress of the active lyric line (0–1), which the marquee uses to decide whether the reveal has reached the right margin.
    /// Returns null without a syllable timeline or when the position cannot be resolved.
    /// </summary>
    private double? ResolveCurrentLyricProgress()
    {
        if (_currentLyricLine is not { } line)
            return null;

        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        return LyricHighlightPolicy.ResolveProgress(line, position);
    }

    /// <summary>
    /// 让跑马灯按"这一行现在要不要跟随滚动"重新决策。擦亮开启或关闭都会改变这一行的推进方式，
    /// 因此两个入口都必须重新应用一次，否则会停留在上一种方式上。
    /// Lets the marquee re-decide whether this line should follow the reveal. Turning the reveal on or off changes how the line advances,
    /// so both entries have to re-apply it or the row would stay in the previous mode.
    /// </summary>
    private void ReapplyLyricMarquee() => ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));

    private void EnsureLyricTextWidth(LyricLine line)
    {
        var fontSize = SongLyrics.FontSize;
        var availableWidth = double.IsFinite(SongLyrics.Width) ? SongLyrics.Width : double.NaN;
        if (ReferenceEquals(_measuredLyricLine, line) &&
            Math.Abs(_measuredLyricFontSize - fontSize) < 0.01 &&
            (double.IsNaN(availableWidth) && double.IsNaN(_measuredLyricAvailableWidth) ||
             Math.Abs(_measuredLyricAvailableWidth - availableWidth) < 0.01))
        {
            return;
        }

        _measuredLyricLine = line;
        _measuredLyricFontSize = fontSize;
        _measuredLyricAvailableWidth = availableWidth;
        _activeLyricTextWidth = MeasureTextWidthExact(SongLyrics.Text, SongLyrics);
    }

    /// <summary>
    /// 计算字形起点相对文本元素左边缘的内缩。
    /// Computes how far the glyph run is inset from the text element's left edge.
    /// </summary>
    private double ResolveLyricInset(TextAlignment alignment)
    {
        var availableWidth = double.IsFinite(SongLyrics.Width) ? SongLyrics.Width : _activeLyricTextWidth;
        var slack = Math.Max(0, availableWidth - _activeLyricTextWidth);
        return alignment switch
        {
            TextAlignment.Center => slack / 2,
            TextAlignment.Right => slack,
            _ => 0
        };
    }

    /// <summary>
    /// 跟随式窗口的内缩：窗口几乎总是比容器宽（起点为 0），只有在整行唱完、窗口滑到末尾而尾巴比容器短时才有留白，
    /// 因此这里用窗口自己的宽度换算，而不是用整行的宽度。
    /// Inset of a follow window: the window is almost always wider than its container (which means a zero inset), and only once the
    /// whole line has been sung and the window has slid to a tail shorter than the container does any slack appear, so this converts
    /// from the window's own width rather than from the whole line's.
    /// </summary>
    /// <param name="alignment">该行的对齐方式。/ Alignment of the line.</param>
    /// <param name="windowWidth">窗口整体宽度（DIP）。/ Total window width in DIP.</param>
    private double ResolveFollowInset(TextAlignment alignment, double windowWidth)
    {
        if (!double.IsFinite(SongLyrics.Width) || !double.IsFinite(windowWidth))
            return 0;

        var slack = Math.Max(0, SongLyrics.Width - windowWidth);
        return alignment switch
        {
            TextAlignment.Center => slack / 2,
            TextAlignment.Right => slack,
            _ => 0
        };
    }

    private double ResolveLyricClipHeight()
    {
        var height = SongLyricsHighlight.ActualHeight;
        if (!double.IsFinite(height) || height <= 0)
        {
            height = SongLyricsContainer.ActualHeight;
        }

        return double.IsFinite(height) && height > 0 ? height : LyricHighlightFallbackHeight;
    }
}
