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
        // 柱数下限是 9：频谱宽度由柱数决定，更少的柱子会让「柱数决定尺寸」这条关系失去意义。
        // The bar-count minimum is nine: the spectrum width follows the count, so fewer bars would defeat the very
        // size relationship the count expresses.
        Assert.AreEqual(new SpectrumComponentSettings(9, 30, 400), new SpectrumComponentSettings(0, 99, 999).Normalize());
        Assert.AreEqual(
            SpectrumStyle.Bars,
            new SpectrumComponentSettings(9, 20, 100) { Style = (SpectrumStyle)99 }.Normalize().Style);
        var performance = new PerformanceComponentSettings([], 1, true).Normalize();
        CollectionAssert.AreEqual(new[] { MetricKind.SystemMemory }, performance.Metrics!.ToArray());
        Assert.AreEqual(500, performance.RefreshIntervalMilliseconds);

        // 采样间隔吸附到 0.5 秒网格再夹取，因此界面读数与滑杆位置永远一致。
        // The sampling interval snaps onto the 0.5-second grid before clamping, so the reading and the slider position
        // always agree.
        Assert.AreEqual(2500, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(2700));
        Assert.AreEqual(2000, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(1800));
        Assert.AreEqual(500, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(120));
        Assert.AreEqual(5000, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(90000));
        Assert.IsTrue(PerformanceComponentSettings.Default.OpenTaskManagerOnClick);
    }

    [TestMethod]
    public void SpectrumBandEdgesStayOrderedAndSpanTheAudibleRange()
    {
        foreach (var bandCount in new[]
                 {
                     SpectrumComponentSettings.MinimumBandCount,
                     12,
                     SpectrumComponentSettings.MaximumBandCount
                 })
        {
            var edges = SpectrumBandPolicy.CreateBandEdges(bandCount);
            Assert.AreEqual(bandCount + 1, edges.Length);
            Assert.AreEqual(SpectrumBandPolicy.FirstBandEdgeHz, edges[0]);
            Assert.AreEqual(SpectrumBandPolicy.LastBandEdgeHz, edges[^1]);
            for (var index = 1; index < edges.Length; index++)
            {
                Assert.IsTrue(edges[index] > edges[index - 1], $"{bandCount} 柱时频段边界必须严格递增。");
            }
        }

        Assert.AreEqual(
            SpectrumComponentSettings.MinimumBandCount,
            SpectrumBandPolicy.CreateBandEdges(1).Length - 1,
            "越界的柱数必须先被夹取。");
    }

    [TestMethod]
    public void SpectrumWidthGrowsWithTheBarCountAndKeepsTheBarWidthFixed()
    {
        var nine = SpectrumPresentationPolicy.CalculateContentWidthDip(SpectrumComponentSettings.MinimumBandCount);
        var twentyFour = SpectrumPresentationPolicy.CalculateContentWidthDip(SpectrumComponentSettings.MaximumBandCount);

        Assert.AreEqual(9 * SpectrumPresentationPolicy.BarWidthDip + 8 * SpectrumPresentationPolicy.BarGapDip, nine, 0.001);
        Assert.AreEqual(24 * SpectrumPresentationPolicy.BarWidthDip + 23 * SpectrumPresentationPolicy.BarGapDip, twentyFour, 0.001);
        Assert.IsTrue(twentyFour > nine);
        Assert.AreEqual(
            nine + SpectrumPresentationPolicy.SurfacePaddingDip * 2,
            SpectrumPresentationPolicy.CalculateSurfaceWidthDip(SpectrumComponentSettings.MinimumBandCount),
            0.001);
        Assert.AreEqual(0, SpectrumPresentationPolicy.ResolveBarLeftDip(0));
        Assert.AreEqual(SpectrumPresentationPolicy.BarPitchDip, SpectrumPresentationPolicy.ResolveBarLeftDip(1));
    }

    [TestMethod]
    public void SpectrumBarAndPixelLevelsKeepAVisibleStubWhenSilent()
    {
        var silent = SpectrumPresentationPolicy.ResolveBarScale(0, 100);
        var loudest = SpectrumPresentationPolicy.ResolveBarScale(1, 100);
        Assert.AreEqual(SpectrumPresentationPolicy.MinimumBarHeightDip / SpectrumPresentationPolicy.ContentHeightDip, silent, 0.0001);
        Assert.AreEqual(1, loudest, 0.0001);
        Assert.IsTrue(SpectrumPresentationPolicy.ResolveBarScale(0.5f, 400) >= SpectrumPresentationPolicy.ResolveBarScale(0.5f, 100));

        Assert.AreEqual(1, SpectrumPresentationPolicy.ResolveLitPixelCount(0, 100));
        Assert.AreEqual(SpectrumPresentationPolicy.PixelDotCount, SpectrumPresentationPolicy.ResolveLitPixelCount(1, 100));
        Assert.IsTrue(SpectrumPresentationPolicy.PixelColumnHeightDip <= SpectrumPresentationPolicy.ContentHeightDip);
        var topPadding = (SpectrumPresentationPolicy.ContentHeightDip - SpectrumPresentationPolicy.PixelColumnHeightDip) / 2;
        Assert.AreEqual(
            topPadding,
            SpectrumPresentationPolicy.ResolvePixelDotTopDip(SpectrumPresentationPolicy.PixelDotCount - 1),
            0.001,
            "一列像素必须垂直居中：最上方方块的留白等于整列居中后的上下偏移。");
        Assert.AreEqual(
            SpectrumPresentationPolicy.ContentHeightDip - topPadding,
            SpectrumPresentationPolicy.ResolvePixelDotTopDip(0) + SpectrumPresentationPolicy.PixelDotHeightDip,
            0.001);
    }

    [TestMethod]
    public void WaveformOutlineIsClosedAndSymmetricAboutTheCentre()
    {
        var bands = new[] { 0f, 0.25f, 0.5f, 1f, 0.75f, 0.2f, 0.9f, 0.1f, 0.4f };
        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(bands, 100);

        Assert.IsTrue(outline.Length > bands.Length * 2, "波形必须在控制点之间插入细分点。");
        Assert.AreEqual(0, outline[0].X, 0.001);
        Assert.AreEqual(
            SpectrumPresentationPolicy.CalculateContentWidthDip(bands.Length),
            outline[(outline.Length / 2) - 1].X,
            0.001,
            "上缘必须从左边界一直画到右边界。");
        foreach (var point in outline)
        {
            Assert.IsTrue(point.Y >= 0 && point.Y <= SpectrumPresentationPolicy.ContentHeightDip);
        }

        // 上缘自左向右、下缘镜像回左：任意序号的点与其镜像点必须关于中线严格对称。
        // The upper edge runs left to right and the lower edge mirrors back left, so a point and its mirror must be
        // exactly symmetric about the centre line.
        var half = outline.Length / 2;
        for (var index = 0; index < half; index++)
        {
            var lower = outline[half + (half - 1 - index)];
            Assert.AreEqual(outline[index].X, lower.X, 0.001);
            Assert.AreEqual(
                SpectrumPresentationPolicy.ContentHeightDip,
                outline[index].Y + lower.Y,
                0.001,
                "波形上下两半必须关于垂直中线对称。");
        }

        Assert.AreEqual(0, SpectrumPresentationPolicy.CreateWaveformOutline([0.5f], 100).Length);
        Assert.IsTrue(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.Waveform));
        Assert.IsTrue(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.MirroredBars));
        Assert.IsFalse(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.Bars));
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
