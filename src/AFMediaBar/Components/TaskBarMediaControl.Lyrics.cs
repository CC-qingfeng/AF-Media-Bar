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
    }

    /// <summary>
    /// 关闭擦亮并还原外观。
    /// Turns highlighting off and restores the appearance.
    /// </summary>
    private void StopLyricHighlight()
    {
        _lyricHighlightTimer.Stop();
        _lyricHighlightActive = false;
        _measuredLyricLine = null;
        SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Collapsed;
        SongLyrics.Opacity = 1;
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

        EnsureLyricTextWidth(line);
        var width = LyricHighlightPolicy.ResolveClipWidth(progress.Value, _activeLyricTextWidth);
        if (width < LyricHighlightMinimumWidth)
        {
            SongLyricsHighlight.Visibility = Visibility.Collapsed;
            SongLyricsHighlightClip.Rect = new Rect(0, 0, 0, ResolveLyricClipHeight());
            return;
        }

        // 裁剪矩形在文本自己的坐标系里，而 TextBlock.Clip 早于 RenderTransform 生效，因此跑马灯的位移会带着擦亮边界一起滚动。
        // The clip rectangle lives in the text's own coordinate space, and TextBlock.Clip applies before RenderTransform, so the
        // marquee translation carries the highlight edge along with the text.
        //
        // 居中或右对齐时字形本身有左内缩，裁剪必须从字形起点开始，否则短句的擦亮会整体偏左。
        // Centred or right-aligned text insets the glyph run, and the clip has to start where the glyphs do; otherwise the reveal
        // of a short line sits too far to the left.
        var left = ResolveLyricInset(SongLyrics.TextAlignment);
        SongLyricsHighlightClip.Rect = new Rect(left, 0, width, ResolveLyricClipHeight());
        SongLyricsHighlight.Visibility = Visibility.Visible;
    }

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
