using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class MotionPolicyTests
{
    [TestMethod]
    public void Resolve_DisabledClientAnimations_IsInstant()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: false, highContrast: false, lowPerformance: false);

        Assert.AreEqual(MotionMode.Instant, profile.Mode);
        Assert.IsFalse(profile.UseTransitions);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
        Assert.AreEqual(TimeSpan.Zero, profile.PositionDuration);
    }

    [TestMethod]
    public void Resolve_HighContrast_UsesReducedMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: true, lowPerformance: false);

        Assert.AreEqual(MotionMode.Reduced, profile.Mode);
        Assert.IsTrue(profile.UseTransitions);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
        Assert.AreEqual(160, profile.PositionDuration.TotalMilliseconds);
    }

    [TestMethod]
    public void Resolve_LowPerformance_UsesReducedMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: true);

        Assert.AreEqual(MotionMode.Reduced, profile.Mode);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
    }

    [TestMethod]
    public void Resolve_NormalDesktop_UsesFullMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: false);

        Assert.AreEqual(MotionMode.Full, profile.Mode);
        Assert.AreEqual(180, profile.StandardDuration.TotalMilliseconds);
        Assert.AreEqual(180, profile.PanelDuration.TotalMilliseconds);
        Assert.AreEqual(220, profile.PositionDuration.TotalMilliseconds);
        Assert.AreEqual(120, profile.ExitDuration.TotalMilliseconds);
        Assert.IsTrue(profile.UseDecorativeEffects);
        Assert.IsTrue(profile.UseContinuousMotion);
    }
}
