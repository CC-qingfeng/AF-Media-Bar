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
                new TaskbarFullPanelSettings(true, false, true, false),
                TaskbarLengthMode.Fixed,
                444)
            {
                MediaTextAlignment = TaskbarMediaTextAlignment.Right,
                SpectrumVisible = false,
                PerformanceVisible = false,
                HoverControls = new TaskbarHoverControlsSettings(true, true, false, false, false)
            },
            Interaction = new GlobalInteractionSettings(
                PlayerClickAction.ActivateSource,
                PlayerClickAction.TogglePlayPause,
                WheelAction.SwitchMediaSource,
                InteractionModifier.RightMouseButton,
                WheelAction.PreviousNext,
                TrayClickAction.OpenSettings,
                TrayWheelBehavior.AdjustVolume,
                TrayWheelBehavior.SwitchOutputDevice),
            TaskbarSurface = new ModeSurfaceSettings(PlayerSurfaceStyle.ThemeTint, 72, 12),
            LyricsTextAlignment = LyricsTextAlignment.Right,
            TrackChangeNotification = new TrackChangeNotificationSettings(
                true,
                true,
                7000,
                TrackChangeNotificationPosition.TopRight,
                NotificationTargetMode.ForegroundWindow,
                @"\\.\DISPLAY3"),
            SmtcSourceFilter = new SmtcSourceFilterSettings(true, ["PLAYER.ONE", "player.two"]),
            QuickLaunch = new QuickLaunchSettings([
                new QuickLaunchEntry("player", "Player", QuickLaunchTargetKind.Executable, @"C:\Apps\Player.exe", "Player.One")]),
            SpectrumComponent = new SpectrumComponentSettings(7, 25, 180),
            PerformanceComponent = new PerformanceComponentSettings(
                [MetricKind.SystemCpu, MetricKind.ProcessMemory], 1800, true),
            Update = new UpdateSettings(
                AutoCheckEnabled: false,
                SkippedVersion: "1.2.0",
                LastCheckUtc: new DateTimeOffset(2026, 9, 16, 8, 30, 0, TimeSpan.Zero),
                LastCheckSucceeded: false)
        };
        using (var writer = new SettingsPersistenceService(_directory)) { writer.Initialize(); SettingsManager.Replace(settings); writer.Flush(); }
        SettingsManager.ResetAll();
        using var reader = new SettingsPersistenceService(_directory);
        reader.Initialize();

        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);
        Assert.AreEqual(LyricsSecondaryLineMode.Translation, SettingsManager.Current.LyricsSecondaryLineMode);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(DynamicIslandEdge.Right, SettingsManager.Current.DynamicIslandEdge);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(120, SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(PlayerClickAction.ActivateSource, SettingsManager.Current.Interaction.ArtworkClickAction);
        Assert.AreEqual(WheelAction.SwitchMediaSource, SettingsManager.Current.Interaction.PrimaryWheelAction);
        Assert.AreEqual(InteractionModifier.RightMouseButton, SettingsManager.Current.Interaction.Modifier);
        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.AreEqual(new TaskbarFullPanelSettings(true, false, true, false), SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.AreEqual(TaskbarLengthMode.Fixed, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(444, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
        Assert.AreEqual(TaskbarMediaTextAlignment.Right, SettingsManager.Current.TaskbarExperience.MediaTextAlignment);
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.SpectrumVisible);
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.PerformanceVisible);
        Assert.AreEqual(72, SettingsManager.Current.TaskbarSurface.BackgroundOpacityPercent);
        Assert.AreEqual(LyricsTextAlignment.Right, SettingsManager.Current.LyricsTextAlignment);
        Assert.AreEqual(@"\\.\DISPLAY2", SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        Assert.AreEqual(7000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.TopRight, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.ForegroundWindow, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual(@"\\.\DISPLAY3", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
        Assert.IsTrue(SettingsManager.Current.SmtcSourceFilter.Enabled);
        CollectionAssert.AreEquivalent(
            new[] { "PLAYER.ONE", "player.two" },
            SettingsManager.Current.SmtcSourceFilter.AllowedSourceIds!.ToArray());
        Assert.AreEqual("Player", SettingsManager.Current.QuickLaunch.Entries!.Single().DisplayName);
        // 柱数 7 低于新的下限 9，写入时被夹取；采样间隔 1800 ms 不在 0.5 秒网格上，被吸附到 2000 ms。
        // A bar count of seven is below the new minimum of nine and is clamped on write, and a 1800 ms interval does not sit
        // on the 0.5-second grid, so it snaps to 2000 ms.
        Assert.AreEqual(new SpectrumComponentSettings(9, 25, 180), SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            new[] { MetricKind.SystemCpu, MetricKind.ProcessMemory },
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2000, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
        Assert.IsTrue(SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick);
        Assert.IsFalse(SettingsManager.Current.Update.AutoCheckEnabled);
        Assert.AreEqual("1.2.0", SettingsManager.Current.Update.SkippedVersion);
        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 16, 8, 30, 0, TimeSpan.Zero),
            SettingsManager.Current.Update.LastCheckUtc);
        Assert.IsFalse(SettingsManager.Current.Update.LastCheckSucceeded);
        var persisted = File.ReadAllText(reader.SettingsPath);
        StringAssert.Contains(persisted, "\"schemaVersion\": 11");
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
        StringAssert.Contains(File.ReadAllText(main), "\"schemaVersion\": 11");
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
        Assert.AreEqual(TaskbarLengthMode.FollowContent, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(TaskbarExperienceSettings.Default.FixedLengthDip, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
        StringAssert.Contains(File.ReadAllText(service.SettingsPath), "\"schemaVersion\": 11");
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
    public void Schema5MissingLengthFieldsUseDocumentedDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":5,\"settings\":{\"taskbarExperience\":{\"hoverLayerEnabled\":true,\"fullLayerEnabled\":true,\"density\":\"Balanced\",\"contentLayout\":\"AdaptiveStack\",\"fullPanel\":{\"mediaInfoVisible\":true,\"mediaControlsVisible\":true}}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(TaskbarLengthMode.FollowContent, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(TaskbarExperienceSettings.Default.FixedLengthDip, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
        Assert.AreEqual(SmtcSourceFilterSettings.Default, SettingsManager.Current.SmtcSourceFilter);
        Assert.AreEqual(SpectrumComponentSettings.Default, SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            PerformanceComponentSettings.Default.Metrics!.ToArray(),
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
    }

    [TestMethod]
    public void Schema6MigratesToTaskbarBindingsAndSeparatesMetadataAlignment()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"schemaVersion":6,"settings":{"windowMode":"DynamicIsland","taskbarExperience":{"hoverLayerEnabled":true,"fullLayerEnabled":true,"density":"Balanced","contentLayout":"CenteredStack","fullPanel":{"mediaInfoVisible":true,"mediaControlsVisible":true}}}}
            """);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(GlobalInteractionSettings.Default, SettingsManager.Current.Interaction);
        Assert.AreEqual(TaskbarContentLayout.AdaptiveStack, SettingsManager.Current.TaskbarExperience.ContentLayout);
        Assert.AreEqual(TaskbarMediaTextAlignment.Center, SettingsManager.Current.TaskbarExperience.MediaTextAlignment);
        Assert.AreEqual(TaskbarHoverControlsSettings.Default, SettingsManager.Current.TaskbarExperience.HoverControls);
    }

    [TestMethod]
    public void Schema7KeepsPreviousTextSizesInsteadOfAdoptingTheNewFontScale()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"schemaVersion":7,"settings":{"taskbarExperience":{"hoverLayerEnabled":true,"fullLayerEnabled":true,"density":"Information","contentLayout":"AdaptiveStack","fullPanel":{"mediaInfoVisible":true,"mediaControlsVisible":true},"lengthMode":"Fixed","fixedLengthDip":420}}}
            """);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(TaskbarExperienceSettings.Default.MediaFontSizePercent,
            SettingsManager.Current.TaskbarExperience.MediaFontSizePercent);
        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.AreEqual(TaskbarLengthMode.Fixed, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(420, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
        StringAssert.Contains(File.ReadAllText(service.SettingsPath), "\"schemaVersion\": 11");
    }

    [TestMethod]
    public void Schema8KeepsItsOwnValuesAndReceivesTheUpdateDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"schemaVersion":8,"settings":{"taskbarExperience":{"hoverLayerEnabled":true,"fullLayerEnabled":true,"density":"Balanced","contentLayout":"AdaptiveStack","fullPanel":{"mediaInfoVisible":true,"mediaControlsVisible":true},"mediaFontSizePercent":125}}}
            """);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(
            125,
            SettingsManager.Current.TaskbarExperience.MediaFontSizePercent,
            "新增更新设置的迁移不得覆盖 schema 8 已有的字号。");
        Assert.AreEqual(
            UpdateSettings.Default,
            SettingsManager.Current.Update,
            "schema 8 的文件没有更新设置，必须取默认值，而不是被推断成关闭。");
        Assert.IsTrue(SettingsManager.Current.Update.AutoCheckEnabled);
        StringAssert.Contains(File.ReadAllText(service.SettingsPath), "\"schemaVersion\": 11");
    }

    /// <summary>
    /// schema 9 的文件里，频谱柱数可能低于新的下限、采样间隔可能落在旧的 250–60000 毫秒区间上，
    /// 而「点击性能组件时打开任务管理器」在旧版本中因为点击被拖动逻辑吞掉而从未真正生效过。
    /// 迁移必须把柱数抬到 9、把间隔吸附并夹取到 0.5–5 秒，并把该开关当作新默认值处理。
    /// A schema 9 file may hold a bar count below the new minimum and a sampling interval anywhere in the old
    /// 250–60000 ms range, and its "open Task Manager on click" switch never actually worked because the click was
    /// swallowed by the drag logic. The migration must lift the bar count to nine, snap and clamp the interval into
    /// 0.5–5 seconds, and treat that switch as the new default.
    /// </summary>
    [TestMethod]
    public void Schema9MigrationWidensSpectrumBarsAndResetsTheTaskManagerClickDefault()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"schemaVersion":9,"settings":{"spectrumComponent":{"bandCount":3,"refreshRateHz":25,"sensitivityPercent":180},"performanceComponent":{"metrics":["SystemCpu"],"refreshIntervalMilliseconds":250,"openTaskManagerOnClick":false}}}
            """);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        var spectrum = SettingsManager.Current.SpectrumComponent;
        Assert.AreEqual(SpectrumComponentSettings.MinimumBandCount, spectrum.BandCount);
        Assert.AreEqual(SpectrumStyle.Bars, spectrum.Style, "旧文件没有样式字段，必须保持柱状图观感。");
        Assert.AreEqual(25, spectrum.RefreshRateHz);
        Assert.AreEqual(180, spectrum.SensitivityPercent);
        Assert.AreEqual(500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
        Assert.IsTrue(
            SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick,
            "该开关在旧版本里从未生效，旧的 false 不代表用户意图，必须取新的默认值。");
        StringAssert.Contains(File.ReadAllText(service.SettingsPath), "\"schemaVersion\": 11");
    }

    /// <summary>
    /// schema 10 的文件没有「完整层入口」字段：迁移必须显式写回开启，保持细杠与悬停按钮都在的既有行为；
    /// 同一批次里字体粗细改用九个真实字重、灵敏度按 10 步进，旧文件里的非网格取值由各自的 Normalize 吸附。
    /// A schema 10 file has no full-layer entry field: the migration must write it back as enabled so the thin bar and the hover
    /// button stay present. In the same batch the font weight moved onto the nine real weights and the sensitivity onto a ten-step
    /// grid, so off-grid values from an older file are snapped by their own Normalize.
    /// </summary>
    [TestMethod]
    public void Schema10MigrationKeepsTheFullPanelEntryAndSnapsWeightAndSensitivity()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"schemaVersion":10,"settings":{"appearance":{"fontWeight":350},"spectrumComponent":{"bandCount":12,"refreshRateHz":20,"sensitivityPercent":7}}}
            """);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(
            SettingsManager.Current.TaskbarExperience.FullPanelEntryVisible,
            "旧文件没有入口开关，迁移后必须仍然显示完整层入口。");
        Assert.AreEqual(400, SettingsManager.Current.Appearance.FontWeight, "350 必须吸附到最近的真实字重 400。");
        Assert.AreEqual(10, SettingsManager.Current.SpectrumComponent.SensitivityPercent, "7 必须抬到步进下限 10。");
        Assert.AreEqual(12, SettingsManager.Current.SpectrumComponent.BandCount);
        StringAssert.Contains(File.ReadAllText(service.SettingsPath), "\"schemaVersion\": 11");
    }

    [TestMethod]
    public void UnimplementedDisplayModeSelectionDoesNotChangeRuntimeModeOrTaskbarSettings()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService());
        var original = SettingsManager.Current.TaskbarExperience;

        viewModel.SwitchToFloatingBallModeCommand.Execute(null);
        viewModel.HoverLayerEnabled = false;

        Assert.IsTrue(viewModel.IsFloatingBallMode);
        Assert.IsTrue(viewModel.IsUnimplementedMode);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(original, SettingsManager.Current.TaskbarExperience);
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
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService());

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
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService());

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
    public void DisplayModesClampsFixedLengthToLiveTaskbarRange()
    {
        SettingsManager.Replace(new AppSettings());
        var constraints = new TaskbarLengthConstraintsService();
        constraints.Update(280, 520);
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), constraints);

        viewModel.FollowMediaTextLength = false;
        viewModel.FixedTaskbarLengthDip = 900;

        Assert.IsTrue(viewModel.UsesFixedTaskbarLength);
        Assert.AreEqual(520, viewModel.FixedTaskbarLengthDip);
        Assert.AreEqual(520, SettingsManager.Current.TaskbarExperience.FixedLengthDip);

        constraints.Update(320, 460);
        Assert.AreEqual(320, viewModel.FixedTaskbarLengthMinimum);
        Assert.AreEqual(460, viewModel.FixedTaskbarLengthMaximum);
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
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        SettingsManager.ResetLayout();
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        SettingsManager.ResetAppearance();
        Assert.AreEqual(AppearanceSettings.Default, SettingsManager.Current.Appearance);

        SettingsManager.Current.Interaction = GlobalInteractionSettings.Default with
        {
            PrimaryWheelAction = WheelAction.SwitchMediaSource
        };
        SettingsManager.Current.TrayWheelBehavior = TrayWheelBehavior.Disabled;
        SettingsManager.ResetInteraction();
        Assert.AreEqual(GlobalInteractionSettings.Default, SettingsManager.Current.Interaction);
        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);

        SettingsManager.Current.TaskbarExperience = TaskbarExperienceSettings.Default with
        {
            FullPanel = TaskbarFullPanelSettings.Compact
        };
        SettingsManager.Current.TrackChangeNotification = TrackChangeNotificationSettings.Default with { Enabled = true };
        SettingsManager.Current.TaskbarTargetMonitorDeviceId = "DISPLAY2";
        SettingsManager.ResetDisplayModes();
        Assert.AreEqual(TaskbarFullPanelSettings.Full, SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.IsNull(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        SettingsManager.Current.SmtcSourceFilter = new SmtcSourceFilterSettings(true, ["player"]);
        SettingsManager.Current.QuickLaunch = new QuickLaunchSettings([
            new QuickLaunchEntry("player", "Player", QuickLaunchTargetKind.AppUserModelId, "Player.App!App")]);
        SettingsManager.Current.SpectrumComponent = new SpectrumComponentSettings(3, 8, 250);
        SettingsManager.Current.PerformanceComponent = new PerformanceComponentSettings([MetricKind.SystemGpu], 900, true);
        SettingsManager.ResetExtraFeatures();
        Assert.AreEqual(TrackChangeNotificationSettings.Default, SettingsManager.Current.TrackChangeNotification);
        Assert.AreEqual(SmtcSourceFilterSettings.Default, SettingsManager.Current.SmtcSourceFilter);
        Assert.AreEqual(0, SettingsManager.Current.QuickLaunch.Entries!.Count);
        Assert.AreEqual(SpectrumComponentSettings.Default, SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            PerformanceComponentSettings.Default.Metrics!.ToArray(),
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
        Assert.IsTrue(SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick);
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
