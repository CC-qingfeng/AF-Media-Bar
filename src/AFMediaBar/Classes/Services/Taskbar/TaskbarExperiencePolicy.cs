using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>信息密度对应的实际组件尺寸。 / Actual component sizes for an information-density preset.</summary>
public readonly record struct TaskbarDensityMetrics(
    double ButtonSize,
    double ProgressWidth,
    double HoverLayerHeight,
    double SectionGap)
{
    public static TaskbarDensityMetrics From(TaskbarInformationDensity density) => density switch
    {
        TaskbarInformationDensity.Minimal => new(22, 76, 36, 6),
        TaskbarInformationDensity.Information => new(28, 118, 42, 10),
        _ => new(24, 96, 40, 8)
    };
}

/// <summary>任务栏媒体呈现、内容宽度和播放进度的纯策略。 / Pure policies for taskbar media presentation, content width, and playback progress.</summary>
public static class TaskbarExperiencePolicy
{
    /// <summary>
    /// 仅在媒体源已连接且正在播放时显示任务栏频谱。
    /// Shows the taskbar spectrum only while a connected media source is playing.
    /// </summary>
    public static bool ShouldShowSpectrum(MediaSnapshot snapshot) =>
        snapshot.IsConnected && snapshot.IsPlaying;

    /// <summary>
    /// 计算横向任务栏媒体条宽度；断开时只保留封面占位和尾部边距，暂停时不预留频谱空间。
    /// Calculates horizontal taskbar media-bar width; disconnected state retains only the artwork
    /// placeholder and trailing margin, while paused media does not reserve spectrum space.
    /// </summary>
    public static double CalculateWidth(
        double measuredTextWidth,
        double artworkRight,
        double spectrumWidth,
        double trailingMargin,
        bool mediaConnected,
        bool spectrumVisible,
        bool transportVisible,
        bool hoverLayerEnabled,
        bool progressVisible,
        TaskbarInformationDensity density,
        double maximumWidth,
        double? componentSpacingDip = null)
    {
        var artworkOnlyWidth = Math.Max(0, artworkRight) + Math.Max(0, trailingMargin);
        if (!mediaConnected)
            return ClampWidth(artworkOnlyWidth, maximumWidth);

        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var hoverWidth = hoverLayerEnabled
            ? CalculateHoverLayerWidth(transportVisible, progressVisible, density, componentSpacingDip)
            : 0;
        var middleWidth = Math.Max(Math.Max(0, measuredTextWidth), hoverWidth);
        var desired = Math.Max(0, artworkRight) +
                       sectionGap +
                       middleWidth +
                       (spectrumVisible ? sectionGap + Math.Max(0, spectrumWidth) : 0) +
                      Math.Max(0, trailingMargin);
        return ClampWidth(desired, maximumWidth);
    }

    private static double ClampWidth(double desired, double maximumWidth) =>
        double.IsFinite(maximumWidth) ? Math.Min(desired, Math.Max(0, maximumWidth)) : desired;

    /// <summary>
    /// 按内容跟随或固定模式解析最终长度，并把固定值限制在当前悬停层下限与可用区间上限之间。
    /// Resolves the final length in content-following or fixed mode and clamps a fixed value
    /// between the current hover-layer minimum and the available-range maximum.
    /// </summary>
    public static double ResolvePrimaryLength(
        double contentLength,
        double minimumLength,
        double maximumLength,
        TaskbarLengthMode mode,
        double fixedLength)
    {
        var minimum = double.IsFinite(minimumLength) ? Math.Max(0, minimumLength) : 0;
        var maximum = double.IsFinite(maximumLength)
            ? Math.Max(minimum, maximumLength)
            : double.PositiveInfinity;
        var content = double.IsFinite(contentLength) ? Math.Max(0, contentLength) : minimum;
        var requested = mode == TaskbarLengthMode.Fixed && double.IsFinite(fixedLength)
            ? fixedLength
            : content;
        return Math.Clamp(requested, minimum, maximum);
    }

    /// <summary>仅在固定长度模式下返回超出文字容器的滚动距离。 / Returns text overflow distance only in fixed-length mode.</summary>
    public static double CalculateMarqueeOverflow(
        double measuredTextLength,
        double availableTextLength,
        TaskbarLengthMode mode)
    {
        if (mode != TaskbarLengthMode.Fixed ||
            !double.IsFinite(measuredTextLength) ||
            !double.IsFinite(availableTextLength))
        {
            return 0;
        }

        return Math.Max(0, measuredTextLength - Math.Max(0, availableTextLength));
    }

    /// <summary>计算中间文字区域容纳悬停控件所需的最小宽度。 / Calculates the middle text region's minimum width for hover controls.</summary>
    public static double CalculateHoverLayerWidth(
        bool transportVisible,
        bool progressVisible,
        TaskbarInformationDensity density,
        double? componentSpacingDip = null)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var buttonCount = transportVisible ? 5 : 2;
        var buttons = buttonCount * metrics.ButtonSize + Math.Max(0, buttonCount - 1) * sectionGap;
        var progress = progressVisible ? sectionGap + metrics.ProgressWidth : 0;
        return 11 + buttons + progress;
    }

    private static double ResolveSectionGap(TaskbarDensityMetrics metrics, double? componentSpacingDip) =>
        componentSpacingDip is { } value && double.IsFinite(value)
            ? Math.Clamp(value, TaskbarExperienceSettings.MinimumComponentSpacingDip, TaskbarExperienceSettings.MaximumComponentSpacingDip)
            : metrics.SectionGap;

    public static double GetPosition(MediaSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Duration <= 0)
            return 0;

        var position = snapshot.Position;
        if (snapshot.IsPlaying && snapshot.TimelineUpdatedAt != DateTimeOffset.MinValue)
        {
            var elapsed = Math.Max(0, (now - snapshot.TimelineUpdatedAt).TotalSeconds);
            position += elapsed * (snapshot.PlaybackRate <= 0 ? 1 : snapshot.PlaybackRate);
        }

        return Math.Clamp(position, 0, snapshot.Duration);
    }
}
