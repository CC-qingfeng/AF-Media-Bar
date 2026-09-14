using System.Windows.Media;
using System.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class PlayerForegroundPolicyTests
{
    [TestMethod]
    public void Resolve_UniformBlack_UsesLightTextWithoutShadow()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.Black));

        Assert.IsNotNull(decision);
        Assert.IsTrue(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_UniformWhite_UsesDarkTextWithoutShadow()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.White));

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_ColorsAroundThreshold_SelectExpectedForeground()
    {
        var darkBackground = PlayerForegroundPolicy.Resolve(Repeated(Color.FromRgb(0x70, 0x70, 0x70)));
        var lightBackground = PlayerForegroundPolicy.Resolve(Repeated(Color.FromRgb(0x88, 0x88, 0x88)));

        Assert.IsTrue(darkBackground?.UsesLightText);
        Assert.IsFalse(lightBackground?.UsesLightText);
    }

    [TestMethod]
    public void Resolve_MixedDarkAndLightBackground_RequestsShadow()
    {
        var samples = Enumerable.Repeat(Colors.Black, 30)
            .Concat(Enumerable.Repeat(Colors.White, 30))
            .ToArray();

        var decision = PlayerForegroundPolicy.Resolve(samples);

        Assert.IsNotNull(decision);
        Assert.IsTrue(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_SmallDarkOutlierSet_DoesNotDisturbWhiteBackground()
    {
        var samples = Enumerable.Repeat(Colors.White, 50)
            .Concat(Enumerable.Repeat(Colors.Black, 5))
            .ToArray();

        var decision = PlayerForegroundPolicy.Resolve(samples);

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_EmptyOrTransparentSamples_ReturnsNoDecision()
    {
        Assert.IsNull(PlayerForegroundPolicy.Resolve([]));
        Assert.IsNull(PlayerForegroundPolicy.Resolve([Colors.Transparent]));
    }

    [TestMethod]
    public void Resolve_NearThreshold_PreservesPreviousChoice()
    {
        var nearThreshold = Repeated(Color.FromRgb(0x7B, 0x7B, 0x7B));

        var keepLight = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(true, false));
        var keepDark = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(false, false));

        Assert.IsTrue(keepLight?.UsesLightText);
        Assert.IsFalse(keepDark?.UsesLightText);
    }

    [TestMethod]
    public void ResolvePresentation_HonorsHighContrastAndForcedModesBeforeAutomaticDecision()
    {
        var automatic = new PlayerForegroundDecision(true, true);

        var highContrast = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: true, themeUsesLightText: false, automatic);
        var forcedLight = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.LightText, highContrast: false, themeUsesLightText: false, automatic);
        var forcedDark = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.DarkText, highContrast: false, themeUsesLightText: true, automatic);

        Assert.IsTrue(highContrast.UsesSystemColors);
        Assert.IsFalse(highContrast.NeedsContrastShadow);
        Assert.IsTrue(forcedLight.UsesLightText);
        Assert.IsFalse(forcedLight.NeedsContrastShadow);
        Assert.IsFalse(forcedDark.UsesLightText);
        Assert.IsFalse(forcedDark.NeedsContrastShadow);
    }

    [TestMethod]
    public void ResolvePresentation_UsesThemeOnlyWhenAutomaticSampleIsUnavailable()
    {
        var fallback = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: true, automaticDecision: null);
        var sampled = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: false,
            new PlayerForegroundDecision(true, true));

        Assert.IsTrue(fallback.UsesLightText);
        Assert.IsFalse(fallback.NeedsContrastShadow);
        Assert.IsTrue(sampled.UsesLightText);
        Assert.IsTrue(sampled.NeedsContrastShadow);
    }

    [TestMethod]
    public void SamplingGeneration_RejectsDisposedOrStaleResults()
    {
        Assert.IsTrue(PlayerForegroundPolicy.IsCurrent(disposed: false, resultGeneration: 4, currentGeneration: 4));
        Assert.IsFalse(PlayerForegroundPolicy.IsCurrent(disposed: true, resultGeneration: 4, currentGeneration: 4));
        Assert.IsFalse(PlayerForegroundPolicy.IsCurrent(disposed: false, resultGeneration: 3, currentGeneration: 4));
    }

    [TestMethod]
    public async Task ScreenSampler_InvalidBounds_ReturnsNoSamples()
    {
        var sampler = new ScreenBackgroundSampler();

        var samples = await sampler.SampleAsync(Int32Rect.Empty, CancellationToken.None);

        Assert.AreEqual(0, samples.Count);
    }

    private static Color[] Repeated(Color color) => Enumerable.Repeat(color, 40).ToArray();
}
