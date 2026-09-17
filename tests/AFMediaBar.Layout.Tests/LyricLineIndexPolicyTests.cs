using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>歌词行定位的纯策略测试。 / Pure policy tests for locating the active lyric line.</summary>
[TestClass]
public sealed class LyricLineIndexPolicyTests
{
    private static IReadOnlyList<LyricLine> Lines =>
    [
        new LyricLine(1, 3, "first"),
        new LyricLine(3, 6, "second"),
        new LyricLine(6, 9, "third")
    ];

    [TestMethod]
    public void BeforeTheFirstLineReturnsMinusOne()
    {
        Assert.AreEqual(-1, LyricLineIndexPolicy.FindIndex(Lines, 0.5));
        Assert.AreEqual(-1, LyricLineIndexPolicy.FindIndex(Lines, -5));
    }

    [TestMethod]
    public void ExactStartAndMiddleResolveToTheirLine()
    {
        Assert.AreEqual(0, LyricLineIndexPolicy.FindIndex(Lines, 1));
        Assert.AreEqual(1, LyricLineIndexPolicy.FindIndex(Lines, 3));
        Assert.AreEqual(1, LyricLineIndexPolicy.FindIndex(Lines, 4.5));
        Assert.AreEqual(2, LyricLineIndexPolicy.FindIndex(Lines, 6));
    }

    [TestMethod]
    public void PastTheEndStaysOnTheLastLine()
    {
        Assert.AreEqual(2, LyricLineIndexPolicy.FindIndex(Lines, 1000));
    }

    [TestMethod]
    public void EmptyCollectionReturnsMinusOne()
    {
        Assert.AreEqual(-1, LyricLineIndexPolicy.FindIndex([], 10));
    }
}
