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

    /// <summary>把九段频谱值应用到任务栏静置层。 / Applies nine spectrum-band values to the taskbar rest layer.</summary>
    public void ApplySpectrum(ReadOnlySpan<float> bands)
    {
        var motion = CurrentMotion;
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        for (var index = 0; index < TaskbarSpectrum.Children.Count; index++)
        {
            if (TaskbarSpectrum.Children[index] is Border bar)
            {
                bar.Visibility = index < settings.BandCount ? Visibility.Visible : Visibility.Collapsed;
                var value = index < bands.Length ? bands[index] * settings.SensitivityPercent / 100f : 0;
                var targetScale = (3 + Math.Clamp(value, 0, 1) * 18) / 21d;
                if (bar.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
                {
                    scale = new ScaleTransform(1, 0.15);
                    bar.RenderTransform = scale;
                }

                scale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    motion.UseContinuousMotion
                        ? new DoubleAnimation
                        {
                            To = targetScale,
                            Duration = motion.FastDuration,
                            EasingFunction = CreateEaseOut()
                        }
                        : null,
                    HandoffBehavior.SnapshotAndReplace);
                if (!motion.UseContinuousMotion)
                    scale.ScaleY = targetScale;
            }
        }
    }

    private void QueueMarqueeUpdate(double textWidth)
    {
        var version = ++_marqueeUpdateVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (version != _marqueeUpdateVersion)
                return;
            UpdateMarqueeAnimations(textWidth);
        }));
    }

    private void UpdateMarqueeAnimations(double textWidth)
    {
        var experience = SettingsManager.Current.TaskbarExperience.Normalize();
        var enabled = _currentMode == WindowMode.Taskbar &&
                      !_isVertical &&
                      _isConnected &&
                      experience.LengthMode == TaskbarLengthMode.Fixed &&
                      CurrentMotion.UseContinuousMotion;
        var fingerprint = $"{enabled}|{textWidth:0.##}|{SongTitle.Text}|{SongArtist.Text}|{SongLyrics.Text}|{SongLyricsSecondary.Text}|{SongMetadataPanel.Visibility}|{SongLyricsPanel.Visibility}|{SongLyricsSecondaryContainer.Visibility}";
        if (fingerprint == _lastMarqueeFingerprint)
            return;

        _lastMarqueeFingerprint = fingerprint;
        ConfigureMarquee(SongTitle, SongTitleContainer, enabled);
        ConfigureMarquee(SongArtist, SongArtistContainer, enabled);
        ConfigureMarquee(SongLyrics, SongLyricsContainer, enabled);
        ConfigureMarquee(SongLyricsSecondary, SongLyricsSecondaryContainer, enabled);
    }

    private static void ConfigureMarquee(TextBlock text, FrameworkElement container, bool enabled)
    {
        if (text.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            text.RenderTransform = transform;
        }
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;

        var available = double.IsFinite(container.ActualWidth) && container.ActualWidth > 0
            ? container.ActualWidth
            : double.IsFinite(container.Width) ? Math.Max(0, container.Width) : 0;
        var measured = MeasureTextWidth(text.Text, text);
        if (!enabled || container.Visibility != Visibility.Visible || available <= 0 || measured <= available + 1)
        {
            text.Width = Math.Max(0, available);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            return;
        }

        text.Width = measured;
        text.TextTrimming = TextTrimming.None;
        var overflow = TaskbarExperiencePolicy.CalculateMarqueeOverflow(
            measured,
            available,
            TaskbarLengthMode.Fixed);
        var travelSeconds = Math.Max(1, overflow / 30d);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2 + travelSeconds * 2),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1 + travelSeconds))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + travelSeconds))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + travelSeconds * 2))));
        transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void StopMarqueeAnimations()
    {
        _marqueeUpdateVersion++;
        _lastMarqueeFingerprint = string.Empty;
        foreach (var text in new[] { SongTitle, SongArtist, SongLyrics, SongLyricsSecondary })
        {
            if (text.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
            }
        }
    }

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
        if (motion.UseDecorativeEffects)
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
            existingBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.Effect = null;
        }

        var targetRadius = motion.UseDecorativeEffects && isCovered ? TaskbarCoveredBlurRadius : 0;
        var targetOpacity = isCovered ? TaskbarCoveredOpacity : 1;
        if (immediate || !motion.UseTransitions)
        {
            blur?.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
            if (blur is not null)
                blur.Radius = targetRadius;
            SongInfoStackPanel.Opacity = targetOpacity;
            return;
        }

        var duration = isCovered ? motion.StandardDuration : motion.FastDuration;
        var easingMode = isCovered ? EasingMode.EaseOut : EasingMode.EaseInOut;
        blur?.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation
        {
            To = targetRadius,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        }, HandoffBehavior.SnapshotAndReplace);
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
        QueueMarqueeUpdate(Math.Max(0, SongInfoStackPanel.Width));
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
        if (_isSeeking)
            return;
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
        if (!immediate && _isSeeking)
            return;
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
