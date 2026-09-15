using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ExtraFeaturePolicyTests
{
    [TestMethod]
    public void SourceAllowListIsApplicationScopedAndCaseInsensitive()
    {
        Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("anything", SmtcSourceFilterSettings.Default));
        var enabled = new SmtcSourceFilterSettings(true, [" Chrome.EXE "]).Normalize();
        Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("chrome.exe", enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("msedge.exe", enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed(null, enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("chrome.exe", new SmtcSourceFilterSettings(true, [])));
    }

    [TestMethod]
    public void QuickLaunchNormalizationDropsInvalidAndDuplicateTargets()
    {
        var normalized = new QuickLaunchSettings([
            new QuickLaunchEntry("a", "One", QuickLaunchTargetKind.Executable, @"C:\\Music\\player.exe"),
            new QuickLaunchEntry("b", "Duplicate", QuickLaunchTargetKind.Executable, @"c:\\music\\PLAYER.exe"),
            new QuickLaunchEntry("c", "Invalid", QuickLaunchTargetKind.Executable, " ")
        ]).Normalize();
        Assert.AreEqual(1, normalized.Entries!.Count);
        Assert.AreEqual("One", normalized.Entries[0].DisplayName);
    }

    [TestMethod]
    public void ComponentSettingsClampAndPerformanceKeepsOneMetric()
    {
        Assert.AreEqual(new SpectrumComponentSettings(1, 30, 400), new SpectrumComponentSettings(0, 99, 999).Normalize());
        var performance = new PerformanceComponentSettings([], 1, true).Normalize();
        CollectionAssert.AreEqual(new[] { MetricKind.SystemMemory }, performance.Metrics!.ToArray());
        Assert.AreEqual(250, performance.RefreshIntervalMilliseconds);
    }

    [TestMethod]
    public void DeferredSelectionWrapsAndMetricsAdvanceEveryThirdSample()
    {
        Assert.AreEqual(2, DeferredCircularSelection.Move(0, 120, 3));
        Assert.AreEqual(0, DeferredCircularSelection.Move(2, -120, 3));
        Assert.AreEqual(0, MetricPresentationPolicy.Advance(0, 2, 3));
        Assert.AreEqual(1, MetricPresentationPolicy.Advance(0, 3, 3));
    }

    [TestMethod]
    public void DisconnectedWidthStillReservesPerformanceComponent()
    {
        var width = TaskbarExperiencePolicy.CalculateWidth(
            0, 44, 38, 4, false, false, false, false, false,
            TaskbarInformationDensity.Balanced, double.PositiveInfinity, 12,
            performanceVisible: true, performanceWidth: 74);
        Assert.AreEqual(44 + 12 + 74 + 4, width, 0.001);
    }
}
