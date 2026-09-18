using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>信息行过滤安全阀的纯策略测试。 / Pure policy tests for the info-line filter's safety valves.</summary>
[TestClass]
public sealed class LyricInfoLinePolicyTests
{
    [TestMethod]
    public void EmptyInputProducesEmptyMask()
    {
        Assert.AreEqual(0, LyricInfoLinePolicy.ResolveDropMask([]).Length);
    }

    [TestMethod]
    public void NothingFlaggedKeepsEveryLine()
    {
        var mask = LyricInfoLinePolicy.ResolveDropMask([false, false, false]);
        CollectionAssert.AreEqual(new[] { false, false, false }, mask);
    }

    [TestMethod]
    public void FlaggedMinorityIsDropped()
    {
        var mask = LyricInfoLinePolicy.ResolveDropMask([true, true, false, false, false, false]);
        CollectionAssert.AreEqual(new[] { true, true, false, false, false, false }, mask);
    }

    [TestMethod]
    public void ExactlyHalfIsStillDropped()
    {
        var mask = LyricInfoLinePolicy.ResolveDropMask([true, true, false, false]);
        CollectionAssert.AreEqual(new[] { true, true, false, false }, mask);
    }

    [TestMethod]
    public void OverHalfFlaggedAbandonsFiltering()
    {
        // 误判比例过高时整体放弃过滤：多显示一行好过清空歌词。
        // A suspiciously high share abandons the filter: one extra line beats an empty display.
        var mask = LyricInfoLinePolicy.ResolveDropMask([true, true, true, false]);
        CollectionAssert.AreEqual(new[] { false, false, false, false }, mask);
    }

    [TestMethod]
    public void EverythingFlaggedAbandonsFiltering()
    {
        var mask = LyricInfoLinePolicy.ResolveDropMask([true, true]);
        CollectionAssert.AreEqual(new[] { false, false }, mask);
    }
}
