using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的频谱呈现：按柱数与样式重建视觉树，并把采样值画上去。
/// Spectrum presentation for the taskbar media control: rebuilds the visual tree for the current bar count and style, then
/// draws the sampled values into it.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>像素柱状图未点亮方块的相对不透明度；留一点亮度才看得出这是一列像素而不是空白。 / Relative opacity of an unlit pixel block; a little brightness is what makes the column read as pixels rather than as empty space.</summary>
    private const double UnlitPixelOpacity = 0.16;

    private readonly List<(Border Bar, ScaleTransform Scale)> _spectrumBars = [];
    private readonly List<List<Border>> _spectrumPixelColumns = [];
    // 用完整限定名，避免与隐式 using 引入的 System.IO.Path 冲突。
    // Fully qualified so it cannot collide with System.IO.Path, which the implicit usings bring in.
    private System.Windows.Shapes.Path? _spectrumWaveform;
    private int _appliedSpectrumBandCount = -1;
    private SpectrumStyle? _appliedSpectrumStyle;

    /// <summary>当前柱数决定的任务栏频谱占用宽度（DIP）。 / Width the taskbar spectrum occupies for the current bar count, in DIP.</summary>
    private double SpectrumSurfaceWidth =>
        SpectrumPresentationPolicy.CalculateSurfaceWidthDip(SettingsManager.Current.SpectrumComponent.Normalize().BandCount);

    /// <summary>
    /// 按当前设置重建频谱视觉树。柱数与样式相同则直接返回，因此每个采样周期调用它是安全的。
    /// Rebuilds the spectrum visual tree for the current settings. It returns immediately while the bar count and style are
    /// unchanged, so calling it once per sampling cycle is safe.
    /// </summary>
    internal void ConfigureSpectrum()
    {
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        if (_appliedSpectrumStyle == settings.Style && _appliedSpectrumBandCount == settings.BandCount)
            return;

        _appliedSpectrumStyle = settings.Style;
        _appliedSpectrumBandCount = settings.BandCount;
        _spectrumBars.Clear();
        _spectrumPixelColumns.Clear();
        _spectrumWaveform = null;
        TaskbarSpectrum.Children.Clear();
        TaskbarSpectrum.Width = SpectrumPresentationPolicy.CalculateContentWidthDip(settings.BandCount);
        TaskbarSpectrum.Height = SpectrumPresentationPolicy.ContentHeightDip;

        switch (settings.Style)
        {
            case SpectrumStyle.Waveform:
                _spectrumWaveform = new System.Windows.Shapes.Path
                {
                    Fill = (Brush)FindResource("TaskbarSpectrumBrush"),
                    Opacity = 0.95,
                    IsHitTestVisible = false
                };
                TaskbarSpectrum.Children.Add(_spectrumWaveform);
                break;
            case SpectrumStyle.PixelBars:
                BuildSpectrumPixelColumns(settings.BandCount);
                break;
            default:
                BuildSpectrumBars(settings.BandCount, settings.Style);
                break;
        }

        // 样式或柱数变化后必须重画一次，否则新的视觉树会停在初始的最小高度上，直到下一次采样到来。
        // A style or bar-count change must be painted once here, otherwise the fresh tree sits at its initial stub height
        // until the next sample arrives.
        ApplySpectrum(ReadOnlySpan<float>.Empty);
    }

    /// <summary>
    /// 把当前采样的频段值应用到任务栏静置层频谱。频段数量由调用方按设置裁剪，因此这里只按现有的视觉子元素绘制。
    /// Applies the current sampled band values to the taskbar rest-layer spectrum. The caller trims the band count to the
    /// settings, so this only draws into the visuals that exist.
    /// </summary>
    /// <param name="bands">归一化频段值（0–1）；空表示静音。/ Normalized band values, 0–1; empty means silence.</param>
    public void ApplySpectrum(ReadOnlySpan<float> bands)
    {
        // 这里再确认一次视觉树与设置一致：首次采样可能早于宿主的设置应用，没有这一步就会往空的画布上画。
        // The tree is re-checked here so it always matches the settings: the first sample can arrive before the host applies
        // its settings, and without this step it would be drawn into an empty canvas.
        ConfigureSpectrum();
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        switch (settings.Style)
        {
            case SpectrumStyle.Waveform:
                ApplyWaveformSpectrum(bands, settings);
                break;
            case SpectrumStyle.PixelBars:
                ApplyPixelSpectrum(bands, settings);
                break;
            default:
                ApplyBarSpectrum(bands, settings);
                break;
        }
    }

    private void BuildSpectrumBars(int bandCount, SpectrumStyle style)
    {
        var initialScale = SpectrumPresentationPolicy.MinimumBarHeightDip / SpectrumPresentationPolicy.ContentHeightDip;
        var symmetric = SpectrumPresentationPolicy.IsSymmetric(style);
        var barStyle = (Style)FindResource("TaskbarSpectrumBar");
        for (var index = 0; index < bandCount; index++)
        {
            var scale = new ScaleTransform(1, initialScale);
            var bar = new Border
            {
                Style = barStyle,
                Width = SpectrumPresentationPolicy.BarWidthDip,
                Height = SpectrumPresentationPolicy.ContentHeightDip,
                // 对称柱状图以垂直中点为原点，因此音量升高时同时向上下延伸；贴底柱状图仍从底边向上长。
                // The symmetric style scales about the vertical centre so a louder band grows both up and down, while the
                // bottom-anchored style keeps growing from the bottom edge.
                RenderTransformOrigin = symmetric ? new Point(0.5, 0.5) : new Point(0.5, 1),
                RenderTransform = scale,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            Canvas.SetTop(bar, 0);
            TaskbarSpectrum.Children.Add(bar);
            _spectrumBars.Add((bar, scale));
        }
    }

    private void BuildSpectrumPixelColumns(int bandCount)
    {
        var pixelStyle = (Style)FindResource("TaskbarSpectrumPixel");
        for (var index = 0; index < bandCount; index++)
        {
            var column = new List<Border>(SpectrumPresentationPolicy.PixelDotCount);
            for (var dotIndex = 0; dotIndex < SpectrumPresentationPolicy.PixelDotCount; dotIndex++)
            {
                var dot = new Border
                {
                    Style = pixelStyle,
                    Width = SpectrumPresentationPolicy.BarWidthDip,
                    Height = SpectrumPresentationPolicy.PixelDotHeightDip,
                    Opacity = UnlitPixelOpacity,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
                Canvas.SetTop(dot, SpectrumPresentationPolicy.ResolvePixelDotTopDip(dotIndex));
                TaskbarSpectrum.Children.Add(dot);
                column.Add(dot);
            }

            _spectrumPixelColumns.Add(column);
        }
    }

    private void ApplyBarSpectrum(ReadOnlySpan<float> bands, SpectrumComponentSettings settings)
    {
        var motion = CurrentMotion;
        for (var index = 0; index < _spectrumBars.Count; index++)
        {
            var (_, scale) = _spectrumBars[index];
            var value = index < bands.Length ? bands[index] : 0;
            var targetScale = SpectrumPresentationPolicy.ResolveBarScale(value, settings.SensitivityPercent);
            if (!motion.UseContinuousMotion)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = targetScale;
                continue;
            }

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation
                {
                    To = targetScale,
                    Duration = motion.FastDuration,
                    EasingFunction = CreateEaseOut()
                },
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void ApplyPixelSpectrum(ReadOnlySpan<float> bands, SpectrumComponentSettings settings)
    {
        for (var index = 0; index < _spectrumPixelColumns.Count; index++)
        {
            var value = index < bands.Length ? bands[index] : 0;
            var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(value, settings.SensitivityPercent);
            var column = _spectrumPixelColumns[index];
            for (var dotIndex = 0; dotIndex < column.Count; dotIndex++)
            {
                // 点阵按整块点亮，因此这里直接写不透明度而不做补间：补间会把方块之间的边界抹成灰阶，像素感随之消失。
                // Blocks light up whole, so the opacity is written directly with no tween: a tween would smear the boundaries
                // between blocks into greys and take the pixel look with it.
                column[dotIndex].Opacity = dotIndex < lit ? 1 : UnlitPixelOpacity;
            }
        }
    }

    private void ApplyWaveformSpectrum(ReadOnlySpan<float> bands, SpectrumComponentSettings settings)
    {
        if (_spectrumWaveform is null)
            return;

        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(bands, settings.SensitivityPercent);
        if (outline.Length == 0)
        {
            _spectrumWaveform.Data = null;
            return;
        }

        var figure = new PathFigure
        {
            StartPoint = new Point(outline[0].X, outline[0].Y),
            IsClosed = true,
            IsFilled = true
        };
        var segment = new PolyLineSegment();
        for (var index = 1; index < outline.Length; index++)
        {
            segment.Points.Add(new Point(outline[index].X, outline[index].Y));
        }

        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _spectrumWaveform.Data = geometry;
    }
}
