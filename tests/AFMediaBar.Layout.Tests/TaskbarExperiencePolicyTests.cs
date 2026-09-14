using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏密度、进度与滚轮映射的纯策略测试。 / Pure policy tests for density, progress, and wheel mapping.</summary>
[TestClass]
public sealed class TaskbarExperiencePolicyTests
{
    [TestMethod]
    public void DensityChangesRealControlMetricsAndMinimumWidth()
    {
        var compact = TaskbarDensityMetrics.From(TaskbarInformationDensity.Minimal);
        var information = TaskbarDensityMetrics.From(TaskbarInformationDensity.Information);
        Assert.IsTrue(information.ButtonSize > compact.ButtonSize);
        Assert.IsTrue(information.ProgressWidth > compact.ProgressWidth);
        Assert.IsTrue(information.HoverLayerHeight > compact.HoverLayerHeight);
        Assert.IsTrue(information.SectionGap > compact.SectionGap);
        Assert.IsTrue(
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, true, true, TaskbarInformationDensity.Information, 900) >
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, true, true, TaskbarInformationDensity.Minimal, 900));
    }

    [TestMethod]
    public void HoverMinimumBelongsToMiddleSectionOnly()
    {
        var middleMinimum = TaskbarExperiencePolicy.CalculateHoverLayerWidth(
            true,
            true,
            TaskbarInformationDensity.Balanced);
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            12,
            44,
            38,
            4,
            true,
            true,
            true,
            true,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + middleMinimum + metrics.SectionGap + 38 + 4, width, 0.001);
    }

    [TestMethod]
    public void DisablingHoverRemovesArtificialMiddleMinimum()
    {
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            12,
            44,
            38,
            4,
            true,
            true,
            true,
            false,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + 12 + metrics.SectionGap + 38 + 4, width, 0.001);
    }

    [TestMethod]
    public void HoverMinimumShrinksWhenTransportOrProgressIsHidden()
    {
        var full = TaskbarExperiencePolicy.CalculateHoverLayerWidth(true, true, TaskbarInformationDensity.Balanced);
        var gestures = TaskbarExperiencePolicy.CalculateHoverLayerWidth(false, true, TaskbarInformationDensity.Balanced);
        var withoutProgress = TaskbarExperiencePolicy.CalculateHoverLayerWidth(false, false, TaskbarInformationDensity.Balanced);
        Assert.IsTrue(full > gestures);
        Assert.IsTrue(gestures > withoutProgress);
    }

    [TestMethod]
    public void SpectrumRequiresConnectedPlayingMedia()
    {
        Assert.IsFalse(TaskbarExperiencePolicy.ShouldShowSpectrum(MediaSnapshot.Disconnected));
        Assert.IsFalse(TaskbarExperiencePolicy.ShouldShowSpectrum(MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = false
        }));
        Assert.IsTrue(TaskbarExperiencePolicy.ShouldShowSpectrum(MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true
        }));
    }

    [TestMethod]
    public void DisconnectedWidthContainsOnlyArtworkAndTrailingMargin()
    {
        var width = TaskbarExperiencePolicy.CalculateWidth(
            120,
            44,
            38,
            4,
            false,
            false,
            true,
            true,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(48, width, 0.001);
    }

    [TestMethod]
    public void PausedMediaWidthDoesNotReserveSpectrumSpace()
    {
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            120,
            44,
            38,
            4,
            true,
            false,
            true,
            false,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + 120 + 4, width, 0.001);
    }

    [TestMethod]
    public void ProgressExtrapolatesWhilePlayingAndClampsToDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            Position = 8,
            Duration = 10,
            PlaybackRate = 1,
            TimelineUpdatedAt = now.AddSeconds(-5)
        };
        Assert.AreEqual(10, TaskbarExperiencePolicy.GetPosition(snapshot, now));
    }

    [TestMethod]
    public void ButtonsDisablePlayerWheel()
    {
        var settings = GlobalInteractionSettings.Default with { Mode = MediaInteractionMode.Buttons };
        Assert.IsNull(GlobalWheelGesturePolicy.Resolve(settings, false, false));
    }

    [TestMethod]
    public void ConfiguredChordOverridesPrimaryWheelAction()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            ChordWheelEnabled = true,
            ChordButton = MouseChordButton.Right,
            PrimaryWheelAction = WheelAction.PreviousNext,
            ChordWheelAction = WheelAction.SwitchMediaSource
        };
        Assert.AreEqual(WheelAction.SwitchMediaSource, GlobalWheelGesturePolicy.Resolve(settings, false, true));
        Assert.AreEqual(WheelAction.PreviousNext, GlobalWheelGesturePolicy.Resolve(settings, true, false));
    }

    [TestMethod]
    public void LegacyAudioWheelActionsNormalizeToPlayerActions()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            PrimaryWheelAction = WheelAction.OutputDevice,
            ChordWheelAction = WheelAction.CurrentApplicationVolume,
            TrayUsesGlobalWheel = true
        };

        var normalized = settings.Normalize();

        Assert.AreEqual(WheelAction.PreviousNext, normalized.PrimaryWheelAction);
        Assert.AreEqual(WheelAction.SwitchMediaSource, normalized.ChordWheelAction);
        Assert.IsFalse(normalized.TrayUsesGlobalWheel);
    }

    [TestMethod]
    public void MediaSourceWheelMovesCircularlyInBothDirections()
    {
        Assert.AreEqual(2, WheelInput.MoveCircular(0, -1, 3));
        Assert.AreEqual(0, WheelInput.MoveCircular(2, 1, 3));
        Assert.AreEqual(1, WheelInput.MoveCircular(0, 4, 3));
    }
}
