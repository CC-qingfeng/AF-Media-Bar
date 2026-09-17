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
    /// 立即应用跑马灯与文字宽度。这里 MUST 同步执行而不是投递到 Loaded 优先级：
    /// 宿主几何刚写完容器宽度，紧接着就是"文字宽度应该是多少"的答案，中间只要插入任何一次几何更新，
    /// 写回的文字宽度就会被重新按可用宽度覆盖，滚动动画于是在一段被裁短的文字上跑。
    /// Applies the marquee and the text widths immediately. This must run synchronously instead of being posted at Loaded priority:
    /// the host geometry has just written the container widths, and the answer to "how wide should the text be" follows right
    /// after — letting any geometry update slip in between overwrites the width back to the available one, leaving the scroll
    /// animation running over text that has already been cut short.
    /// </summary>
    /// <param name="availableWidth">文字区的可用宽度（DIP）。/ Available width of the text region in DIP.</param>
    internal void ApplyMarqueeLayout(double availableWidth) => UpdateMarqueeAnimations(availableWidth);

    /// <summary>
    /// 应用跑马灯与文字宽度。宿主几何算出的可用宽度直接传进来，不再依赖 <c>ActualWidth</c>：那个值在宽度刚写入时可能还是上一轮的，
    /// 用它判断"是否溢出"会把超出文字误判成放得下，于是既不滚动、也没有省略号，看起来就是被硬裁。
    /// Applies the marquee and the text widths. The available width the host geometry computed is passed in directly instead of being
    /// read from <c>ActualWidth</c>: that value can still belong to the previous pass right after a width is written, and judging
    /// "does it overflow" from it misclassifies an overflowing text as fitting, which neither scrolls nor shows an ellipsis and
    /// simply looks hard-cut.
    /// </summary>
    /// <param name="availableWidth">文字区的可用宽度（DIP），由几何唯一计算。/ Available width of the text region in DIP, computed by the geometry alone.</param>
    private void UpdateMarqueeAnimations(double availableWidth)
    {
        // 跑马灯的判据是"文字真的超出了可用宽度"，而不是长度模式：跟随内容模式下媒体栏被任务栏安全上限夹住时，
        // 文字同样会超出，此时也必须能滚动看全，否则用户只能看到被截断的标题。
        // The marquee keys off the text actually overflowing its available width rather than off the length mode: in
        // follow-content mode the bar is clamped by the taskbar's safe maximum, the text overflows there too, and it has to be
        // scrollable as well, otherwise the user is left with a truncated title.
        var enabled = _currentMode == WindowMode.Taskbar &&
                      !_isVertical &&
                      _isConnected &&
                      CurrentMotion.UseContinuousMotion;
        var fingerprint = $"{enabled}|{availableWidth:0.##}|{SongTitle.Text}|{SongArtist.Text}|{SongLyrics.Text}|{SongLyricsSecondary.Text}|{SongMetadataPanel.Visibility}|{SongLyricsPanel.Visibility}|{SongLyricsSecondaryContainer.Visibility}";
        // 只有"要不要重开滚动"由指纹决定；文字宽度与裁剪方式每次都要写回。宿主几何按可用宽度重置过 TextBlock 宽度，
        // 若这里因为指纹相同而整段跳过，滚动仍在继续，但文字已被硬裁到可用宽度——这正是"滚动时看不到超出部分"的根因。
        // Only "should the scroll restart" is decided by the fingerprint; the text width and trimming are written back every
        // time. The host's geometry resets the TextBlock width to the available width, and skipping this whole method on an
        // unchanged fingerprint left the scroll continuing over text that had already been hard-cut to that width — the actual
        // reason the overflow never came into view.
        var restartAnimation = fingerprint != _lastMarqueeFingerprint;
        _lastMarqueeFingerprint = fingerprint;
        _marqueeEntries.Clear();
        AddMarqueeEntry(SongTitle, SongTitleContainer, enabled, availableWidth);
        AddMarqueeEntry(SongArtist, SongArtistContainer, enabled, availableWidth);
        AddMarqueeEntry(SongLyrics, SongLyricsContainer, enabled, availableWidth);
        AddMarqueeEntry(SongLyricsSecondary, SongLyricsSecondaryContainer, enabled, availableWidth);
        if (_marqueeEntries.Count == 0)
        {
            _marqueeTimer.Stop();
            return;
        }

        if (restartAnimation)
            StartMarqueeScroll();
    }

    private void AddMarqueeEntry(
        TextBlock text,
        FrameworkElement container,
        bool enabled,
        double availableWidth)
    {
        if (ConfigureMarquee(text, container, enabled, availableWidth) is { } entry)
            _marqueeEntries.Add(entry);
    }

    private static MarqueeEntry? ConfigureMarquee(
        TextBlock text,
        FrameworkElement container,
        bool enabled,
        double availableWidth)
    {
        if (text.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            text.RenderTransform = transform;
        }

        var available = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        var measured = MeasureTextWidth(text.Text, text);
        var overflow = enabled &&
                       container.Visibility == Visibility.Visible &&
                       available > 0
            ? TaskbarExperiencePolicy.CalculateMarqueeOverflow(measured, available)
            : 0;
        if (overflow <= 1)
        {
            transform.X = 0;
            text.Width = Math.Max(0, available);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            return null;
        }

        // 宽度必须给足整段文字：只有比容器宽，位移才能把后面那截带进裁剪区。
        // The full text width has to be granted: only a text wider than its container lets the translation bring the tail into
        // the clipped area.
        text.Width = measured;
        text.TextTrimming = TextTrimming.None;
        return new MarqueeEntry(text, transform, overflow);
    }

    /// <summary>
    /// 跑马灯的滚动由本控件按帧推进，而不是交给 Storyboard：速度、停留与折返都在 <see cref="MarqueeScrollPolicy"/> 里，
    /// 因此"文字到底有没有移动、移了多远"是可以直接读出来的量，也不再依赖动画时钟是否被渲染目标驱动。
    /// The marquee is advanced by this control frame by frame instead of by a storyboard: speed, pauses, and the turn-around all live
    /// in <see cref="MarqueeScrollPolicy"/>, so "did the text move, and how far" is directly readable and no longer depends on an
    /// animation clock being driven by a rendering target.
    /// </summary>
    private void AdvanceMarqueeScroll()
    {
        if (_marqueeEntries.Count == 0)
        {
            _marqueeTimer.Stop();
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_marqueeStartedAt);
        foreach (var entry in _marqueeEntries)
            entry.Transform.X = MarqueeScrollPolicy.CalculateOffset(entry.Overflow, elapsed);
    }

    private void StartMarqueeScroll()
    {
        _marqueeStartedAt = Stopwatch.GetTimestamp();
        foreach (var entry in _marqueeEntries)
            entry.Transform.X = 0;
        if (!_marqueeTimer.IsEnabled)
            _marqueeTimer.Start();
    }

    private void StopMarqueeAnimations()
    {
        _lastMarqueeFingerprint = string.Empty;
        _marqueeEntries.Clear();
        _marqueeTimer.Stop();
        foreach (var text in new[] { SongTitle, SongArtist, SongLyrics, SongLyricsSecondary })
        {
            if (text.RenderTransform is TranslateTransform transform)
                transform.X = 0;
        }
    }

    /// <summary>一个正在滚动的文字元素：它的位移与需要移动的距离。 / One scrolling text element: its transform and the distance it has to travel.</summary>
    private readonly record struct MarqueeEntry(TextBlock Text, TranslateTransform Transform, double Overflow);

    private static double MeasureTextWidth(string text, TextBlock source)
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
        return formatted.WidthIncludingTrailingWhitespace + 4;
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
