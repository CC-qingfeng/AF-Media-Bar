using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 强调色调色板的纯逻辑测试：系统强调色只影响颜色，不涉及资源写入或窗口。
/// Pure-logic tests for the accent palette: the system accent only affects colors, never resources or windows.
/// </summary>
[TestClass]
public sealed class AccentColorPolicyTests
{
    private static readonly Color Pink = Color.FromRgb(0xC5, 0x4A, 0x85);
    private static readonly Color DefaultBlue = Color.FromRgb(0x00, 0x78, 0xD4);

    [TestMethod]
    public void Build_KeepsTheRawAccentAndStripsAlpha()
    {
        var palette = AccentColorPolicy.Build(Color.FromArgb(0x80, Pink.R, Pink.G, Pink.B), darkTheme: true, highContrast: false);

        Assert.AreEqual(Pink, palette.Accent);
        Assert.AreEqual(255, palette.Accent.A);
    }

    [TestMethod]
    public void Build_DarkThemeLightensHoverAndDarkensPressed()
    {
        var palette = AccentColorPolicy.Build(Pink, darkTheme: true, highContrast: false);

        Assert.IsTrue(palette.Hover.G > palette.Accent.G, "hover should move toward white in a dark theme");
        Assert.IsTrue(palette.Pressed.R < palette.Accent.R, "pressed should move toward black in a dark theme");
        Assert.IsTrue(palette.Tint.A < palette.Accent.A, "the tint must stay translucent");
        Assert.AreEqual(Pink.R, palette.Tint.R);
    }

    [TestMethod]
    public void Build_LightThemeMovesInTheOppositeDirection()
    {
        var palette = AccentColorPolicy.Build(DefaultBlue, darkTheme: false, highContrast: false);

        Assert.IsTrue(palette.Hover.B < palette.Accent.B, "hover should move toward black in a light theme");
        Assert.IsTrue(palette.Pressed.R > palette.Accent.R, "pressed should move toward white in a light theme");
    }

    [TestMethod]
    public void Build_IsDeterministicForTheSameInput()
    {
        var first = AccentColorPolicy.Build(Pink, darkTheme: true, highContrast: false);
        var second = AccentColorPolicy.Build(Pink, darkTheme: true, highContrast: false);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Build_HighContrastAlwaysUsesSystemHighlightColors()
    {
        var palette = AccentColorPolicy.Build(Pink, darkTheme: true, highContrast: true);

        Assert.AreEqual(SystemColors.HighlightColor.R, palette.Accent.R);
        Assert.AreEqual(SystemColors.HighlightColor.G, palette.Accent.G);
        Assert.AreEqual(SystemColors.HighlightColor.B, palette.Accent.B);
        Assert.AreEqual(SystemColors.HighlightTextColor.R, palette.OnAccent.R);
    }

    [TestMethod]
    public void ResolveOnAccent_PicksTheReadableSideForBothExtremes()
    {
        Assert.AreEqual(Colors.White, AccentColorPolicy.ResolveOnAccent(Color.FromRgb(0x20, 0x20, 0x80)));
        Assert.AreEqual(Color.FromRgb(0x1C, 0x1C, 0x1C), AccentColorPolicy.ResolveOnAccent(Color.FromRgb(0xF5, 0xF0, 0xC0)));
    }

    [TestMethod]
    public void ResolveRequested_UsesTheCustomAccentOnlyWhenItParses()
    {
        var system = Color.FromRgb(0x53, 0x78, 0xB1);

        Assert.AreEqual(
            Pink,
            AccentColorPolicy.ResolveRequested(system, AccentColorMode.Custom, "#C54A85"));
        Assert.AreEqual(
            system,
            AccentColorPolicy.ResolveRequested(system, AccentColorMode.System, "#C54A85"));
        // 自选色打了一半或写坏了 MUST 回退系统色：把无法解析的值送进调色板会让每个强调色画刷变成透明。
        // A half-typed or corrupt custom colour MUST fall back to the system one: feeding an unparsable value into the palette
        // would turn every accent brush transparent.
        Assert.AreEqual(
            system,
            AccentColorPolicy.ResolveRequested(system, AccentColorMode.Custom, "#C5"));
        Assert.AreEqual(
            system,
            AccentColorPolicy.ResolveRequested(system, AccentColorMode.Custom, string.Empty));
    }

    [TestMethod]
    public void ColorHex_RoundTripsAndRejectsAnythingElse()
    {
        Assert.IsTrue(ColorHex.TryParse("#C54A85", out var parsed));
        Assert.AreEqual(Pink, parsed);
        Assert.AreEqual("#C54A85", ColorHex.Format(parsed));
        // 大小写与缺少 # 都接受；三位缩写、颜色名与非法字符都拒绝。
        // Upper or lower case and a missing # are accepted; the 3-digit shorthand, colour names, and invalid characters are not.
        Assert.IsTrue(ColorHex.TryParse("c54a85", out var lower));
        Assert.AreEqual(Pink, lower);
        Assert.IsTrue(ColorHex.TryParse("#FFC54A85", out var withAlpha));
        Assert.AreEqual(Pink, withAlpha);
        Assert.IsFalse(ColorHex.TryParse("#C54", out _));
        Assert.IsFalse(ColorHex.TryParse("Pink", out _));
        Assert.IsFalse(ColorHex.TryParse("#GGGGGG", out _));
        Assert.IsFalse(ColorHex.TryParse(null, out _));
    }
}
