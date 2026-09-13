using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AFMediaBar.ViewModels.Pages;
using System.Windows;

namespace AFMediaBar.Layout.Tests;

/// <summary>设置持久化服务的纯文件和恢复测试。 / Pure file and recovery tests for settings persistence.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsPersistenceServiceTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "AFMediaBarTests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void TearDown()
    {
        SettingsManager.ResetAll();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        if (File.Exists(_directory)) File.Delete(_directory);
    }

    [TestMethod]
    public void RoundTripPersistsAllFieldsAndStringEnums()
    {
        var settings = new AppSettings
        {
            Appearance = AppearanceSettings.Default with { FontWeight = 700, BackdropMode = ApplicationBackdropMode.Acrylic },
            TrayWheelBehavior = TrayWheelBehavior.Disabled,
            LyricsEnabled = false,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLineMode = LyricsSecondaryLineMode.Translation,
            TaskbarBarEnabled = false,
            TaskbarTargetMonitorDeviceId = @"\\.\DISPLAY2",
            Position = TaskbarBarPosition.End,
            TaskbarBarBackgroundBlur = true,
            TaskbarBarManualPadding = 14,
            WindowMode = WindowMode.DynamicIsland,
            LayoutOrientationMode = LayoutOrientationMode.Vertical,
            LayoutLengthScalePercent = 115,
            LayoutThicknessScalePercent = 85,
            DynamicIslandBackgroundMode = DynamicIslandBackgroundMode.Transparent,
            TaskbarBarCrossAxisOffsetDip = -10,
            TaskbarBarAvoidIcons = false,
            TaskbarBarPositionLocked = true,
            DynamicIslandLeft = 120,
            DynamicIslandTop = 240,
            DynamicIslandEdge = DynamicIslandEdge.Right,
            DynamicIslandEdgeDocked = false,
            TaskbarExperience = new TaskbarExperienceSettings(
                false,
                true,
                TaskbarInformationDensity.Information,
                TaskbarContentLayout.CenteredStack,
                new TaskbarFullPanelSettings(true, false, true, false)),
            Interaction = new GlobalInteractionSettings(MediaInteractionMode.Gestures, WheelAction.OutputDevice, true, MouseChordButton.Right, WheelAction.CurrentApplicationVolume, TrayClickAction.OpenSettings, false),
            TaskbarSurface = new ModeSurfaceSettings(PlayerSurfaceStyle.ThemeTint, 72, 12),
            LyricsTextAlignment = LyricsTextAlignment.Right,
            TrackChangeNotification = new TrackChangeNotificationSettings(
                true,
                true,
                7000,
                TrackChangeNotificationPosition.TopRight,
                NotificationTargetMode.ForegroundWindow,
                @"\\.\DISPLAY3")
        };
        using (var writer = new SettingsPersistenceService(_directory)) { writer.Initialize(); SettingsManager.Replace(settings); writer.Flush(); }
        SettingsManager.ResetAll();
        using var reader = new SettingsPersistenceService(_directory);
        reader.Initialize();

        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);
        Assert.AreEqual(LyricsSecondaryLineMode.Translation, SettingsManager.Current.LyricsSecondaryLineMode);
        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        Assert.AreEqual(DynamicIslandEdge.Right, SettingsManager.Current.DynamicIslandEdge);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(120, SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(MediaInteractionMode.Gestures, SettingsManager.Current.Interaction.Mode);
        Assert.AreEqual(MouseChordButton.Right, SettingsManager.Current.Interaction.ChordButton);
        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.AreEqual(new TaskbarFullPanelSettings(true, false, true, false), SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.AreEqual(72, SettingsManager.Current.TaskbarSurface.BackgroundOpacityPercent);
        Assert.AreEqual(LyricsTextAlignment.Right, SettingsManager.Current.LyricsTextAlignment);
        Assert.AreEqual(@"\\.\DISPLAY2", SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        Assert.AreEqual(7000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.TopRight, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.ForegroundWindow, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual(@"\\.\DISPLAY3", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
        var persisted = File.ReadAllText(reader.SettingsPath);
        StringAssert.Contains(persisted, "\"schemaVersion\": 4");
        StringAssert.Contains(persisted, "\"Disabled\"");
    }

    [TestMethod]
    public void MissingFieldsUseDefaultsAndInvalidValuesNormalize()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{\"schemaVersion\":1,\"settings\":{\"layoutLengthScalePercent\":999,\"layoutThicknessScalePercent\":null,\"taskbarBarCrossAxisOffsetDip\":-999,\"dynamicIslandLeft\":-1,\"windowMode\":\"bad\",\"trayWheelBehavior\":\"bad\"}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(125, SettingsManager.Current.LayoutLengthScalePercent);
        Assert.AreEqual(100, SettingsManager.Current.LayoutThicknessScalePercent);
        Assert.AreEqual(-20, SettingsManager.Current.TaskbarBarCrossAxisOffsetDip);
        Assert.IsNull(SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(TrayWheelBehavior.SwitchOutputDevice, SettingsManager.Current.TrayWheelBehavior);
        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
        Assert.AreEqual(GlobalInteractionSettings.Default, SettingsManager.Current.Interaction);
        Assert.AreEqual(TaskbarFullPanelSettings.Full, SettingsManager.Current.TaskbarExperience.FullPanel);
    }

    [TestMethod]
    public void CorruptMainRecoversBackupAndQuarantinesMain()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "not-json");
        File.WriteAllText(main + ".bak", "{\"schemaVersion\":1,\"settings\":{\"lyricsEnabled\":false}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
        Assert.IsTrue(Directory.GetFiles(_directory, "settings.json.invalid-*").Length == 1);
        Assert.IsTrue(File.Exists(main));
    }

    [TestMethod]
    public void UnsupportedSchemaIsQuarantinedAndDefaultsAreWritten()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "{\"schemaVersion\":99,\"settings\":{\"lyricsEnabled\":false}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
        Assert.IsTrue(Directory.GetFiles(_directory, "settings.json.unsupported-*").Length == 1);
        StringAssert.Contains(File.ReadAllText(main), "\"schemaVersion\": 4");
    }

    [TestMethod]
    public void Schema2MigratesFullPanelVisibilityToFullPreset()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":2,\"settings\":{\"taskbarExperience\":{\"hoverLayerEnabled\":false,\"fullLayerEnabled\":true,\"density\":\"Information\",\"contentLayout\":\"CenteredStack\"}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.HoverLayerEnabled);
        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.AreEqual(TaskbarFullPanelSettings.Full, SettingsManager.Current.TaskbarExperience.FullPanel);
    }

    [TestMethod]
    public void FullPanelPresetsAndInvalidAllHiddenValueNormalizePredictably()
    {
        Assert.AreEqual(new TaskbarFullPanelSettings(true, true, false, false), TaskbarFullPanelSettings.Compact);
        Assert.AreEqual(new TaskbarFullPanelSettings(true, true, true, true), TaskbarFullPanelSettings.Full);
        Assert.AreEqual(
            TaskbarFullPanelSettings.Compact,
            new TaskbarFullPanelSettings(false, false, false, false).Normalize());
    }

    [TestMethod]
    public void Schema3AllHiddenFullPanelValueFallsBackToCompactPreset()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":3,\"settings\":{\"taskbarBarSelectedMonitor\":1,\"taskbarExperience\":{\"hoverLayerEnabled\":true,\"fullLayerEnabled\":true,\"density\":\"Balanced\",\"contentLayout\":\"AdaptiveStack\",\"fullPanel\":{\"mediaInfoVisible\":false,\"mediaControlsVisible\":false,\"audioControlsVisible\":false,\"performanceVisible\":false}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(TaskbarFullPanelSettings.Compact, SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.AreEqual(TrackChangeNotificationSettings.Default, SettingsManager.Current.TrackChangeNotification);
        Assert.AreEqual(1, service.LegacyTaskbarMonitorIndex);
        Assert.AreEqual(
            "DISPLAY2",
            DisplayTargetPolicy.ResolveLegacyDeviceId(
                [Monitor("DISPLAY1", true), Monitor("DISPLAY2", false)],
                service.LegacyTaskbarMonitorIndex!.Value));
    }

    [TestMethod]
    public void Schema4InvalidNotificationValuesNormalizeToSafeDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":4,\"settings\":{\"trackChangeNotification\":{\"enabled\":true,\"showWhenFullscreen\":false,\"durationMilliseconds\":60000,\"position\":\"bad\",\"targetMode\":99,\"fixedMonitorDeviceId\":\"  DISPLAY2  \"}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.AreEqual(10000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.BottomLeft, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.Fixed, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual("DISPLAY2", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
    }

    [TestMethod]
    public void Schema4PartialNotificationUsesDocumentedDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":4,\"settings\":{\"trackChangeNotification\":{\"enabled\":true}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.IsFalse(SettingsManager.Current.TrackChangeNotification.ShowWhenFullscreen);
        Assert.AreEqual(1000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.BottomLeft, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.Fixed, SettingsManager.Current.TrackChangeNotification.TargetMode);
    }

    [TestMethod]
    public void DisplayModesProtectsLastFullPanelGroupAndRecognizesPresets()
    {
        SettingsManager.Replace(new AppSettings
        {
            TaskbarExperience = TaskbarExperienceSettings.Default with
            {
                FullPanel = new TaskbarFullPanelSettings(true, false, false, false)
            }
        });
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService());

        viewModel.FullPanelMediaInfoVisible = false;
        Assert.IsTrue(viewModel.FullPanelMediaInfoVisible);
        Assert.IsFalse(viewModel.CanToggleFullPanelMediaInfo);

        viewModel.FullPanelMediaControlsVisible = true;
        viewModel.FullPanelMediaInfoVisible = false;
        Assert.IsFalse(viewModel.FullPanelMediaInfoVisible);
        Assert.AreEqual("自定义", viewModel.FullPanelLayoutStatus);

        viewModel.FullPanelMediaInfoVisible = true;
        Assert.AreEqual("紧凑", viewModel.FullPanelLayoutStatus);
        viewModel.FullPanelAudioControlsVisible = true;
        viewModel.FullPanelPerformanceVisible = true;
        Assert.AreEqual("完整", viewModel.FullPanelLayoutStatus);

        viewModel.ApplyCompactFullPanelPresetCommand.Execute(null);
        Assert.AreEqual("紧凑", viewModel.FullPanelLayoutStatus);
        viewModel.ApplyFullFullPanelPresetCommand.Execute(null);
        Assert.AreEqual("完整", viewModel.FullPanelLayoutStatus);
    }

    [TestMethod]
    public void DisplayModesUpdatesIndependentNotificationAndTaskbarTargets()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService());

        viewModel.TrackChangeNotificationEnabled = true;
        viewModel.ShowTrackChangeNotificationWhenFullscreen = true;
        viewModel.TrackChangeNotificationDurationSeconds = 6;
        viewModel.TrackChangeNotificationPosition = TrackChangeNotificationPosition.TopCenter;
        viewModel.TrackChangeNotificationTargetMode = NotificationTargetMode.Fixed;
        viewModel.TrackChangeNotificationFixedMonitorDeviceId = "DISPLAY2";
        viewModel.TaskbarTargetMonitorDeviceId = "DISPLAY1";

        Assert.AreEqual("DISPLAY2", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
        Assert.AreEqual("DISPLAY1", SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        Assert.AreEqual(6000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.TopCenter, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.ShowWhenFullscreen);
        Assert.IsTrue(viewModel.CanSelectTrackChangeNotificationMonitor);
    }

    [TestMethod]
    public void ResetScopesOnlyChangeTheirOwnedFields()
    {
        SettingsManager.Replace(new AppSettings
        {
            LyricsEnabled = false,
            Appearance = AppearanceSettings.Default with { FontWeight = 700 },
            WindowMode = WindowMode.DynamicIsland,
            DynamicIslandLeft = 50
        });
        SettingsManager.ResetGeneral();
        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        SettingsManager.ResetLayout();
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        SettingsManager.ResetAppearance();
        Assert.AreEqual(AppearanceSettings.Default, SettingsManager.Current.Appearance);

        SettingsManager.Current.TaskbarExperience = TaskbarExperienceSettings.Default with
        {
            FullPanel = TaskbarFullPanelSettings.Compact
        };
        SettingsManager.Current.TrackChangeNotification = TrackChangeNotificationSettings.Default with { Enabled = true };
        SettingsManager.Current.TaskbarTargetMonitorDeviceId = "DISPLAY2";
        SettingsManager.ResetDisplayModes();
        Assert.AreEqual(TaskbarFullPanelSettings.Full, SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.AreEqual(TrackChangeNotificationSettings.Default, SettingsManager.Current.TrackChangeNotification);
        Assert.IsNull(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
    }

    [TestMethod]
    public void DebounceAndFlushPersistLatestValue()
    {
        using var service = new SettingsPersistenceService(_directory, TimeSpan.FromMilliseconds(50));
        service.Initialize();
        SettingsManager.Current.LyricsEnabled = false;
        SettingsManager.Current.LyricsEnabled = true;
        Thread.Sleep(150);
        var text = File.ReadAllText(service.SettingsPath);
        StringAssert.Contains(text, "\"lyricsEnabled\": true");
    }

    [TestMethod]
    public void SaveFailureKeepsInMemorySettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "blocks-directory-creation");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();
        SettingsManager.Current.LyricsEnabled = false;
        service.Flush();
        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
    }

    [TestMethod]
    public void ResetEventsRefreshSingletonViewModelState()
    {
        SettingsManager.Replace(new AppSettings { WindowMode = WindowMode.DynamicIsland, LayoutLengthScalePercent = 125 });
        var viewModel = new LayoutViewModel();
        var layoutEvents = 0;
        EventHandler<LayoutSettingsChangedEventArgs> handler = (_, _) => layoutEvents++;
        SettingsManager.LayoutSettingsChanged += handler;
        try
        {
            SettingsManager.ResetLayout();
            Assert.AreEqual(WindowMode.Taskbar, viewModel.CurrentWindowMode);
            Assert.AreEqual(100, viewModel.LayoutLengthScalePercent);
            Assert.AreEqual(1, layoutEvents);
        }
        finally { SettingsManager.LayoutSettingsChanged -= handler; }
    }

    private static DisplayMonitorInfo Monitor(string id, bool primary) =>
        new(id, id, primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 96, 96);

    private sealed class FakeDisplayMonitorService : IDisplayMonitorService
    {
        private readonly IReadOnlyList<DisplayMonitorInfo> _monitors =
            [Monitor("DISPLAY1", true), Monitor("DISPLAY2", false)];

        public event EventHandler? MonitorsChanged;

        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => _monitors;

        public void Refresh() => MonitorsChanged?.Invoke(this, EventArgs.Empty);

        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) =>
            DisplayTargetPolicy.ResolveFixed(_monitors, deviceId);

        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) =>
            ResolveFixedMonitor(fixedDeviceId);

        public bool IsForegroundWindowFullscreen() => false;
    }
}
