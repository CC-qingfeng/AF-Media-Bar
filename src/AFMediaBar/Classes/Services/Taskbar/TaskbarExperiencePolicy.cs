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
    /// 媒体源连接期间始终为任务栏频谱保留位置；暂停时由采样器清零，避免文字区跳动。
    /// Reserves taskbar spectrum space while media is connected; sampling clears paused bars without shifting text.
    /// </summary>
    public static bool ShouldShowSpectrum(MediaSnapshot snapshot) =>
        snapshot.IsConnected;

    /// <summary>
    /// 计算横向任务栏媒体条宽度；连接期间固定预留频谱位置，断开时移除该位置。
    /// Calculates horizontal taskbar media-bar width, reserving spectrum space for every connected state.
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
        double? componentSpacingDip = null,
        bool performanceVisible = false,
        double performanceWidth = 0,
        TaskbarHoverControlsSettings? hoverControls = null)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var performance = performanceVisible ? sectionGap + Math.Max(0, performanceWidth) : 0;
        var artworkOnlyWidth = Math.Max(0, artworkRight) + performance + Math.Max(0, trailingMargin);
        if (!mediaConnected)
            return ClampWidth(artworkOnlyWidth, maximumWidth);

        var hoverWidth = hoverLayerEnabled
            ? CalculateHoverLayerWidth(
                hoverControls ?? new TaskbarHoverControlsSettings(
                    transportVisible,
                    transportVisible,
                    true,
                    true,
                    progressVisible),
                progressVisible,
                density,
                componentSpacingDip)
            : 0;
        var middleWidth = Math.Max(Math.Max(0, measuredTextWidth), hoverWidth);
        var desired = Math.Max(0, artworkRight) +
                       sectionGap +
                       middleWidth +
                       (spectrumVisible ? sectionGap + Math.Max(0, spectrumWidth) : 0) +
                       performance +
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

    /// <summary>
    /// 返回文字超出容器的滚动距离。判据只有"量出来的文字比可用宽度长"，与长度模式无关：
    /// 跟随内容模式下媒体栏被任务栏安全上限夹住时文字同样会超出，那时也必须能滚动看全。
    /// Returns the distance the text overflows its container. The only condition is that the measured text is wider than the
    /// available width, independent of the length mode: in follow-content mode the bar is clamped by the taskbar's safe maximum
    /// and the text overflows there too, and it has to stay scrollable.
    /// </summary>
    /// <param name="measuredTextLength">量出的文字宽度。/ Measured text width.</param>
    /// <param name="availableTextLength">容器可用的文字宽度。/ Text width available inside the container.</param>
    public static double CalculateMarqueeOverflow(double measuredTextLength, double availableTextLength)
    {
        if (!double.IsFinite(measuredTextLength) || !double.IsFinite(availableTextLength))
            return 0;

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

    /// <summary>按独立控制显隐计算悬停层最小宽度。 / Calculates hover width from independently visible controls.</summary>
    public static double CalculateHoverLayerWidth(
        TaskbarHoverControlsSettings controls,
        bool progressAvailable,
        TaskbarInformationDensity density,
        double? componentSpacingDip = null)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var buttonCount = (controls.PlayPauseVisible ? 1 : 0) +
                          (controls.PreviousNextVisible ? 2 : 0) +
                          (controls.OutputDeviceVisible ? 1 : 0) +
                          (controls.AudioControlVisible ? 1 : 0);
        var buttons = buttonCount == 0 ? 0 : buttonCount * metrics.ButtonSize + (buttonCount - 1) * sectionGap;
        var progress = controls.ProgressVisible && progressAvailable ? (buttonCount > 0 ? sectionGap : 0) + metrics.ProgressWidth : 0;
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
