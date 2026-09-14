using System.Windows.Media;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>根据实际背景计算出的播放器前景决策。/ Player-foreground decision calculated from the actual background.</summary>
public readonly record struct PlayerForegroundDecision(bool UsesLightText, bool NeedsContrastShadow);

/// <summary>播放器最终采用的前景呈现方式。/ Final foreground presentation used by the player.</summary>
public readonly record struct PlayerForegroundPresentation(
    bool UsesSystemColors,
    bool UsesLightText,
    bool NeedsContrastShadow);

/// <summary>
/// 根据背景像素、用户模式和高对比度状态选择播放器文字前景。
/// Selects the player text foreground from background pixels, user mode, and high-contrast state.
/// </summary>
public static class PlayerForegroundPolicy
{
    private const double RequiredContrast = 4.5;
    private const double Hysteresis = 0.025;
    private static readonly double DarkTextLuminance = RelativeLuminance(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly double SwitchingLuminance =
        Math.Sqrt(1.05 * (DarkTextLuminance + 0.05)) - 0.05;

    /// <summary>判断异步采样结果是否仍属于当前宿主代际。/ Determines whether an asynchronous sample still belongs to the current host generation.</summary>
    public static bool IsCurrent(bool disposed, int resultGeneration, int currentGeneration) =>
        !disposed && resultGeneration == currentGeneration;

    /// <summary>
    /// 从背景样本计算自动文字色；没有有效样本时返回 null。
    /// Calculates an automatic text color from background samples, or null when no valid sample exists.
    /// </summary>
    /// <param name="samples">不透明或半透明背景颜色样本。/ Opaque or translucent background color samples.</param>
    /// <param name="previous">上一次有效决定，用于阈值迟滞。/ Previous valid decision used for threshold hysteresis.</param>
    public static PlayerForegroundDecision? Resolve(
        IReadOnlyList<Color> samples,
        PlayerForegroundDecision? previous = null)
    {
        if (samples.Count == 0)
            return null;

        var luminances = samples
            .Where(color => color.A > 0)
            .Select(CompositeOnBlackAndCalculateLuminance)
            .OrderBy(value => value)
            .ToArray();
        if (luminances.Length == 0)
            return null;

        var median = Percentile(luminances, 0.5);
        var usesLightText = previous?.UsesLightText ?? median <= SwitchingLuminance;
        if (previous is { UsesLightText: true } && median > SwitchingLuminance + Hysteresis)
            usesLightText = false;
        else if (previous is { UsesLightText: false } && median < SwitchingLuminance - Hysteresis)
            usesLightText = true;

        // Bright patches are the weakest points for white text; dark patches are the weakest
        // points for dark text. Robust percentiles ignore a small number of glyph/icon samples.
        var adverseLuminance = usesLightText
            ? Percentile(luminances, 0.8)
            : Percentile(luminances, 0.2);
        var contrast = usesLightText
            ? 1.05 / (adverseLuminance + 0.05)
            : (adverseLuminance + 0.05) / (DarkTextLuminance + 0.05);

        return new PlayerForegroundDecision(usesLightText, contrast < RequiredContrast);
    }

    /// <summary>
    /// 按高对比度、强制模式、自动采样和主题回退的优先级生成最终呈现。
    /// Resolves final presentation using high contrast, forced mode, automatic sampling, then theme fallback.
    /// </summary>
    public static PlayerForegroundPresentation ResolvePresentation(
        PlayerForegroundMode mode,
        bool highContrast,
        bool themeUsesLightText,
        PlayerForegroundDecision? automaticDecision)
    {
        if (highContrast)
            return new PlayerForegroundPresentation(true, themeUsesLightText, false);

        return mode switch
        {
            PlayerForegroundMode.LightText => new PlayerForegroundPresentation(false, true, false),
            PlayerForegroundMode.DarkText => new PlayerForegroundPresentation(false, false, false),
            _ when automaticDecision is { } decision =>
                new PlayerForegroundPresentation(false, decision.UsesLightText, decision.NeedsContrastShadow),
            _ => new PlayerForegroundPresentation(false, themeUsesLightText, false)
        };
    }

    private static double CompositeOnBlackAndCalculateLuminance(Color color)
    {
        if (color.A == byte.MaxValue)
            return RelativeLuminance(color);

        var alpha = color.A / 255.0;
        return RelativeLuminance(Color.FromRgb(
            (byte)Math.Round(color.R * alpha),
            (byte)Math.Round(color.G * alpha),
            (byte)Math.Round(color.B * alpha)));
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);

    private static double ToLinear(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];
        var fraction = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }
}
