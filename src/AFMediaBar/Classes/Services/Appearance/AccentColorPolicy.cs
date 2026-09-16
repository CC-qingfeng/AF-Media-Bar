using System.Windows;
using System.Windows.Media;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 应用统一的强调色调色板：一个原始强调色加上由它派生的悬停、按下、淡色与"强调色上的文字色"。
/// The application's unified accent palette: the raw accent plus derived hover, pressed, tint, and on-accent text colors.
/// </summary>
public readonly record struct AccentPalette(Color Accent, Color Hover, Color Pressed, Color Tint, Color OnAccent);

/// <summary>
/// 从系统强调色派生整套调色板，不执行资源写入或窗口重应用。
/// Derives the whole accent palette from the system accent color without touching resources or windows.
/// </summary>
public static class AccentColorPolicy
{
    /// <summary>淡色底的透明度（约 12%），用于选中卡片、芯片与图示底片。/ Alpha of the tinted surface (about 12%) used by selected cards, chips, and diagram plates.</summary>
    private const byte TintAlpha = 31;

    /// <summary>悬停与按下档相对原始强调色的混合比例。/ Mix ratio of the hover and pressed shades against the raw accent.</summary>
    private const double HoverMix = 0.12;
    private const double PressedMix = 0.16;

    /// <summary>
    /// 由系统强调色、主题明暗与高对比度状态生成调色板。
    /// Builds the palette from the system accent color, theme brightness, and high-contrast state.
    /// </summary>
    /// <param name="systemAccent">系统原始强调色（可带 alpha，函数只取其 RGB）。/ Raw system accent; only its RGB channels are used.</param>
    /// <param name="darkTheme">当前是否为深色主题，决定悬停/按下往哪个方向混合。/ Whether the theme is dark, which decides the direction of the hover and pressed mixes.</param>
    /// <param name="highContrast">是否处于高对比度；此时整套颜色改用系统高亮色。/ Whether high contrast is active, in which case the system highlight colors are used.</param>
    public static AccentPalette Build(Color systemAccent, bool darkTheme, bool highContrast)
    {
        if (highContrast)
        {
            // 高对比度下强调色必须交出控制权：轨道、填充与文字色都取系统高亮色，避免与系统调色板冲突。
            // High contrast takes precedence: track, fill, and text colors come from the system highlight colors so the
            // application cannot fight the system palette.
            var highlight = Opaque(SystemColors.HighlightColor);
            var highlightText = Opaque(SystemColors.HighlightTextColor);
            return new AccentPalette(highlight, highlight, highlight, WithAlpha(highlight, TintAlpha), highlightText);
        }

        var accent = Opaque(systemAccent);
        var toward = darkTheme ? Colors.White : Colors.Black;
        return new AccentPalette(
            accent,
            Mix(accent, toward, HoverMix),
            Mix(accent, darkTheme ? Colors.Black : Colors.White, PressedMix),
            WithAlpha(accent, TintAlpha),
            ResolveOnAccent(accent));
    }

    /// <summary>
    /// 在强调色底上选择可读的文字色：按相对亮度在近黑与纯白之间取对比度更高的一侧。
    /// Picks a readable text color on an accent fill: the higher-contrast side between near-black and white.
    /// </summary>
    public static Color ResolveOnAccent(Color accent)
    {
        var luminance = RelativeLuminance(accent);
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        var dark = Color.FromRgb(0x1C, 0x1C, 0x1C);
        var contrastWithDark = (luminance + 0.05) / (RelativeLuminance(dark) + 0.05);
        return contrastWithWhite >= contrastWithDark ? Colors.White : dark;
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color left, Color right, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(left.R + (right.R - left.R) * ratio),
            (byte)Math.Round(left.G + (right.G - left.G) * ratio),
            (byte)Math.Round(left.B + (right.B - left.B) * ratio));
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
}
