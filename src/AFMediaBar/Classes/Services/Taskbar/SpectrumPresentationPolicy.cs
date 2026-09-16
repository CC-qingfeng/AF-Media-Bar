using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>频谱几何空间中的一个采样点。 / One sample point in the spectrum's geometry space.</summary>
/// <param name="X">相对频谱内容区左边界的横向偏移（DIP）。/ Horizontal offset from the spectrum content's left edge, in DIP.</param>
/// <param name="Y">相对频谱内容区上边界的纵向偏移（DIP）。/ Vertical offset from the spectrum content's top edge, in DIP.</param>
public readonly record struct SpectrumPoint(double X, double Y);

/// <summary>
/// 任务栏频谱的几何与样式换算。柱宽、柱距和高度都是固定值，因此组件宽度完全由柱数决定——这正是柱数与尺寸相关的那条关系。
/// Geometry and style math for the taskbar spectrum. Bar width, gap, and height are fixed, so the component width follows
/// the bar count alone; that is exactly the size relationship the bar count is supposed to express.
/// </summary>
public static class SpectrumPresentationPolicy
{
    /// <summary>单根柱子的宽度（DIP）。 / Width of one bar in DIP.</summary>
    public const double BarWidthDip = 2;

    /// <summary>相邻柱子之间的净间距（DIP）。 / Clear gap between adjacent bars in DIP.</summary>
    public const double BarGapDip = 2;

    /// <summary>柱距（柱宽加净间距，DIP）。 / Bar pitch: bar width plus clear gap, in DIP.</summary>
    public const double BarPitchDip = BarWidthDip + BarGapDip;

    /// <summary>频谱内容区的高度（DIP）。 / Height of the spectrum content area in DIP.</summary>
    public const double ContentHeightDip = 21;

    /// <summary>频谱悬停表面在内容区四周的留白（DIP）。 / Padding the spectrum hover surface adds around the content in DIP.</summary>
    public const double SurfacePaddingDip = 1;

    /// <summary>像素柱状图中单个方块的高度（DIP）。 / Height of one block in the pixel bar chart, in DIP.</summary>
    public const double PixelDotHeightDip = 2;

    /// <summary>像素柱状图中相邻方块之间的净间距（DIP）。 / Clear gap between blocks in the pixel bar chart, in DIP.</summary>
    public const double PixelDotGapDip = 1;

    /// <summary>柱状图与对称柱状图的最小柱高（DIP）；静音时只剩这一小段，用于保留槽位。 / Minimum bar height in DIP for the bar styles; only this stub remains while silent, which keeps the slot visible.</summary>
    public const double MinimumBarHeightDip = 3;

    /// <summary>波形在垂直中线上下各自允许的最大振幅（DIP）。 / Maximum amplitude of the waveform above and below its vertical centre, in DIP.</summary>
    public const double WaveformMaximumHalfHeightDip = (ContentHeightDip - 1) / 2;

    /// <summary>波形每两个控制点之间插入的细分段数；频段很少时它决定曲线是否平滑。 / Interpolation segments per control-point gap; with few bands this is what makes the curve read as a waveform.</summary>
    public const int WaveformSegmentsPerGap = 6;

    /// <summary>像素柱状图每列包含的方块数量。 / Number of blocks in each pixel column.</summary>
    public static int PixelDotCount { get; } =
        (int)((ContentHeightDip + PixelDotGapDip) / (PixelDotHeightDip + PixelDotGapDip));

    /// <summary>像素柱状图一列的实际高度（DIP）。 / Actual height of one pixel column in DIP.</summary>
    public static double PixelColumnHeightDip { get; } =
        PixelDotCount * (PixelDotHeightDip + PixelDotGapDip) - PixelDotGapDip;

    /// <summary>频谱内容区的宽度（DIP）；柱数为一时没有柱距。 / Width of the spectrum content area in DIP; a single bar has no pitch to add.</summary>
    /// <param name="bandCount">柱数；越界时先被夹取。/ Bar count; clamped to the persisted range first.</param>
    public static double CalculateContentWidthDip(int bandCount)
    {
        var count = SpectrumBandPolicy.ClampBandCount(bandCount);
        return count * BarWidthDip + (count - 1) * BarGapDip;
    }

    /// <summary>频谱悬停表面的宽度（DIP），等于内容宽度加两侧留白。 / Width of the spectrum hover surface in DIP: content width plus the padding on both sides.</summary>
    /// <param name="bandCount">柱数；越界时先被夹取。/ Bar count; clamped to the persisted range first.</param>
    public static double CalculateSurfaceWidthDip(int bandCount) =>
        CalculateContentWidthDip(bandCount) + SurfacePaddingDip * 2;

    /// <summary>第 index 根柱子相对内容区左边界的偏移（DIP）。 / Offset of the bar at the given index from the content's left edge, in DIP.</summary>
    /// <param name="index">从零开始的柱序号。/ Zero-based bar index.</param>
    public static double ResolveBarLeftDip(int index) => Math.Max(0, index) * BarPitchDip;

    /// <summary>
    /// 把归一化音量换算成柱子的纵向缩放比例。静音时返回最小柱高对应的比例而不是零，暂停时才会留下固定的一小段。
    /// Converts a normalized level into a vertical bar scale. Silence maps to the minimum-height ratio rather than zero, so a
    /// paused spectrum keeps a visible stub instead of an empty slot.
    /// </summary>
    /// <param name="value">采样器给出的归一化音量（0–1）。/ Normalized level from the sampler, 0–1.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    public static double ResolveBarScale(float value, int sensitivityPercent)
    {
        var level = Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f);
        return (MinimumBarHeightDip + level * (ContentHeightDip - MinimumBarHeightDip)) / ContentHeightDip;
    }

    /// <summary>
    /// 把归一化音量换算成像素柱点亮的方块数量。至少点亮一个方块，理由与柱状图保留最小柱高相同。
    /// Converts a normalized level into the number of lit pixel blocks. At least one block stays lit, for the same reason the
    /// bar styles keep a minimum bar height.
    /// </summary>
    /// <param name="value">采样器给出的归一化音量（0–1）。/ Normalized level from the sampler, 0–1.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    public static int ResolveLitPixelCount(float value, int sensitivityPercent)
    {
        var level = Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f);
        var lit = (int)Math.Round(level * PixelDotCount, MidpointRounding.AwayFromZero);
        return Math.Clamp(lit, 1, PixelDotCount);
    }

    /// <summary>像素柱状图中第 dotIndex 个方块（自下而上）相对内容区上边界的偏移（DIP）。 / Offset from the content's top edge of the block at the given bottom-up index, in DIP.</summary>
    /// <param name="dotIndex">自下而上、从零开始的方块序号（0 是最下面一块）。/ Zero-based bottom-up block index, 0 being the lowest block.</param>
    public static double ResolvePixelDotTopDip(int dotIndex)
    {
        var index = Math.Clamp(dotIndex, 0, PixelDotCount - 1);
        var verticalOffset = (ContentHeightDip - PixelColumnHeightDip) / 2;
        return verticalOffset + (PixelDotCount - 1 - index) * (PixelDotHeightDip + PixelDotGapDip);
    }

    /// <summary>
    /// 生成波形样式的闭合轮廓：先沿上缘自左向右，再沿下缘镜像回左，形成一个围绕垂直中线对称的带状图形。
    /// Builds the closed outline of the waveform style: first along the upper edge from left to right, then mirrored back
    /// along the lower edge, producing a ribbon symmetric about the vertical centre line.
    /// </summary>
    /// <param name="bands">采样器给出的频段值。/ Band values from the sampler.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <returns>轮廓点；频段少于两个时返回空数组，调用方应隐藏该样式。/ Outline points, or an empty array when fewer than two bands are available; callers hide the style in that case.</returns>
    public static SpectrumPoint[] CreateWaveformOutline(ReadOnlySpan<float> bands, int sensitivityPercent)
    {
        if (bands.Length < 2)
            return [];

        var count = bands.Length;
        var contentWidth = CalculateContentWidthDip(count);
        var step = contentWidth / (count - 1);
        var halfHeights = new double[count];
        for (var index = 0; index < count; index++)
        {
            var level = Math.Clamp(bands[index] * sensitivityPercent / 100f, 0f, 1f);
            halfHeights[index] = level * WaveformMaximumHalfHeightDip;
        }

        var upper = Interpolate(halfHeights, step);
        var outline = new SpectrumPoint[upper.Length * 2];
        const double centre = ContentHeightDip / 2;
        for (var index = 0; index < upper.Length; index++)
        {
            outline[index] = new SpectrumPoint(upper[index].X, centre - upper[index].Y);
        }

        // 下缘用同一组采样点镜像回去，因此两条边在任何音量下都严格对称，不会出现偏向一侧的波形。
        // The lower edge mirrors the same samples, so both edges stay exactly symmetric at every level instead of leaning to
        // one side.
        for (var index = 0; index < upper.Length; index++)
        {
            var mirrored = upper[upper.Length - 1 - index];
            outline[upper.Length + index] = new SpectrumPoint(mirrored.X, centre + mirrored.Y);
        }

        return outline;
    }

    /// <summary>
    /// 对半振幅控制点做 Catmull-Rom 插值。频段数量最少只有九个，控制点之间不插值就是一条折线，
    /// 因此这一段是波形样式与柱状样式的唯一区别所在。
    /// Runs Catmull-Rom interpolation over the half-amplitude control points. With as few as nine bands the raw control
    /// points form a polyline, so this interpolation is what separates the waveform style from the bar styles.
    /// </summary>
    private static SpectrumPoint[] Interpolate(double[] halfHeights, double step)
    {
        var count = halfHeights.Length;
        var segments = WaveformSegmentsPerGap * (count - 1);
        var points = new SpectrumPoint[segments + 1];
        for (var segment = 0; segment < count - 1; segment++)
        {
            var p0 = halfHeights[Math.Max(0, segment - 1)];
            var p1 = halfHeights[segment];
            var p2 = halfHeights[segment + 1];
            var p3 = halfHeights[Math.Min(count - 1, segment + 2)];
            for (var stepIndex = 0; stepIndex < WaveformSegmentsPerGap; stepIndex++)
            {
                var t = stepIndex / (double)WaveformSegmentsPerGap;
                var y = 0.5 * ((2 * p1) +
                               (-p0 + p2) * t +
                               (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                               (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                points[segment * WaveformSegmentsPerGap + stepIndex] =
                    new SpectrumPoint((segment + t) * step, Math.Clamp(y, 0, WaveformMaximumHalfHeightDip));
            }
        }

        points[segments] = new SpectrumPoint((count - 1) * step, Math.Clamp(halfHeights[count - 1], 0, WaveformMaximumHalfHeightDip));
        return points;
    }

    /// <summary>频谱样式是否以垂直中线为对称轴（波形与对称柱状图）。 / Whether a spectrum style is symmetric about the vertical centre line (waveform and mirrored bars).</summary>
    /// <param name="style">频谱样式。/ Spectrum style.</param>
    public static bool IsSymmetric(SpectrumStyle style) =>
        style is SpectrumStyle.Waveform or SpectrumStyle.MirroredBars;
}
