using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
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
}
