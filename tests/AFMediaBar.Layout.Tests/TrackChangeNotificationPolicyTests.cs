using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Media;

namespace AFMediaBar.Layout.Tests;

/// <summary>曲目身份、暂停候选与呈现门控测试。 / Track identity, paused-candidate, and presentation-gate tests.</summary>
[TestClass]
public sealed class TrackChangeNotificationPolicyTests
{
    [TestMethod]
    public void FirstValidSnapshotOnlyEstablishesBaseline()
    {
        var policy = new TrackChangeNotificationPolicy();

        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "First", true)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "First", false)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "First", true)));
    }

    [TestMethod]
    public void PausedTrackChangeWaitsForFirstPlayingSnapshot()
    {
        var policy = new TrackChangeNotificationPolicy();
        policy.ShouldNotify(Snapshot("source-a", "First", true));

        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "Second", false)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "Second", false, artist: "Artist arrived")));
        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-a", "Second", true, artist: "Artist arrived")));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-a", "Second", true)));
    }

    [TestMethod]
    public void PlayingTrackAndSourceChangesEachNotifyOnce()
    {
        var policy = new TrackChangeNotificationPolicy();
        policy.ShouldNotify(Snapshot("source-a", "First", true));

        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-a", "Second", true)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot(
            "source-a",
            " second ",
            true,
            artist: "Late artist",
            position: 8,
            artwork: new DrawingImage())));
        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-b", "Second", true)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-b", "Second", false)));
        Assert.IsFalse(policy.ShouldNotify(Snapshot("source-b", "Second", true)));
    }

    [TestMethod]
    public void RapidTrackChangesRemainDistinctRequests()
    {
        var policy = new TrackChangeNotificationPolicy();
        policy.ShouldNotify(Snapshot("source-a", "First", true));

        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-a", "Second", true)));
        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-a", "Third", true)));
        Assert.IsTrue(policy.ShouldNotify(Snapshot("source-a", "Fourth", true)));
    }

    [TestMethod]
    public void LateArtworkAndArtistKeepTheSameObservableIdentity()
    {
        var initial = Snapshot("source-a", "New track", true, artist: string.Empty);
        var enriched = Snapshot(
            "source-a",
            "New track",
            true,
            artist: "Artist arrived",
            artwork: new DrawingImage());

        Assert.AreEqual(
            TrackChangeNotificationPolicy.CreateIdentity(initial),
            TrackChangeNotificationPolicy.CreateIdentity(enriched));

        var policy = new TrackChangeNotificationPolicy();
        Assert.IsFalse(policy.ShouldNotify(initial));
        Assert.IsFalse(policy.ShouldNotify(enriched));
    }

    [TestMethod]
    public void DisabledAndFullscreenPoliciesSuppressPresentation()
    {
        var defaults = TrackChangeNotificationSettings.Default;
        Assert.IsFalse(TrackChangeNotificationPresentationPolicy.ShouldPresent(defaults, false));

        var enabled = defaults with { Enabled = true };
        Assert.IsTrue(TrackChangeNotificationPresentationPolicy.ShouldPresent(enabled, false));
        Assert.IsFalse(TrackChangeNotificationPresentationPolicy.ShouldPresent(enabled, true));
        Assert.IsTrue(TrackChangeNotificationPresentationPolicy.ShouldPresent(
            enabled with { ShowWhenFullscreen = true },
            true));
    }

    private static MediaSnapshot Snapshot(
        string sourceId,
        string title,
        bool isPlaying,
        string artist = "Artist",
        double position = 0,
        ImageSource? artwork = null) => new(
            true,
            isPlaying,
            true,
            true,
            true,
            title,
            artist,
            sourceId,
            "Source",
            artwork,
            null,
            position,
            180,
            true,
            false,
            MediaRepeatMode.Unavailable,
            1,
            DateTimeOffset.UtcNow);
}
