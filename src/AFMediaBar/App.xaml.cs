using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Pages;
using AFMediaBar.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.DependencyInjection;

namespace AFMediaBar
{
    /// <summary>
    /// 应用程序入口：负责 DI 容器构建、服务注册和应用生命周期管理。
    /// Application entry point: responsible for DI container setup, service registration, and lifecycle management.
    ///
    /// 职责 Responsibilities:
    /// 1. 配置依赖注入容器（Services、ViewModels、Views）
    ///    Configure dependency injection container (Services, ViewModels, Views)
    /// 2. 启动 ApplicationHostService 创建主窗口
    ///    Start ApplicationHostService to create the main window
    /// 3. 处理应用启动和退出事件
    ///    Handle application startup and exit events
    /// </summary>
    public partial class App
    {
        private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(5);
        private int _exitHandled;

        // .NET Generic Host 提供依赖注入、配置、日志等服务。
        // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                // === WPF-UI 导航服务 WPF-UI Navigation Service ===
                services.AddNavigationViewPageProvider();

                // === 应用生命周期宿主服务 Application Lifecycle Host Service ===
                services.AddHostedService<ApplicationHostService>();

                // === 核心服务层 Core Service Layer ===
                // 主题管理（深浅色主题切换）Theme management (light/dark theme switching)
                services.AddSingleton<IThemeService, ThemeService>();

                // WPF-UI 任务栏状态服务（不创建 Shell 通知区域图标）
                // WPF-UI taskbar-state service (does not create a Shell notification icon)
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // 显示器目录为通知、设置和任务栏停靠提供统一的设备标识解析。
                // The display catalog provides shared device-identity resolution for notifications,
                // settings, and taskbar docking.
                services.AddSingleton<IDisplayMonitorService, DisplayMonitorService>();

                // 任务栏停靠引擎（将媒体栏嵌入到资源管理器任务栏）
                // Taskbar docking engine (embeds the media bar into the Explorer taskbar)
                services.AddSingleton<ITaskbarDockService, TaskbarDockService>();
                services.AddSingleton<ITaskbarOccupiedAreaProbe, TaskbarOccupiedAreaProbe>();
                services.AddSingleton<TaskbarOccupiedAreaService>();
                services.AddSingleton<TaskbarLengthConstraintsService>();

                // SMTC 媒体会话监听服务，生成 MediaSnapshot 快照供 UI 消费
                // SMTC media session monitoring service, producing MediaSnapshot for UI consumption
                services.AddSingleton<LyricsService>(_ => new LyricsService(
                    new NetEaseLyricsProvider(),
                    new LrclibLyricsProvider()));
                services.AddSingleton<MediaSessionCatalog>();
                services.AddSingleton<MediaSessionSelectionService>();
                services.AddSingleton<MediaSnapshotBuilder>();
                services.AddSingleton<IMediaSourceProvider, NetEaseMediaProvider>();
                services.AddSingleton<MediaSourceActivationService>();
                services.AddSingleton<MediaSessionService>();
                services.AddSingleton<TrackChangeNotificationCoordinator>();
                services.AddSingleton<MediaSourceProcessResolver>();
                services.AddSingleton<AudioProcessInfoService>();
                services.AddSingleton<ApplicationIconService>();
                services.AddSingleton<ApplicationVolumeService>();
                services.AddSingleton<AudioDeviceService>();
                services.AddSingleton<AudioInteractionService>();
                services.AddSingleton<GlobalInteractionRouter>();
                services.AddSingleton<AudioMonitorService>();
                services.AddSingleton<SystemMetricsService>();
                services.AddSingleton<SystemMetricsMonitorService>();
                services.AddSingleton<SpatialAudioService>();
                // 自有 Shell 托盘图标与统一鼠标输入监听
                // App-owned Shell tray icon and unified mouse input monitor
                services.AddSingleton<ShellTrayIconService>();
                services.AddSingleton<NativeMouseInputMonitor>();
                services.AddSingleton<NativeWindowBackdropAdapter>();
                services.AddSingleton<WindowAppearanceService>();
                services.AddSingleton<ScreenBackgroundSampler>();
                services.AddSingleton<SettingsPersistenceService>();

                // 安装协调互斥体：只让安装程序能识别"程序正在运行"，不改变单实例行为。
                // Install-coordination mutex: lets the installer notice a running instance without changing single-instance behaviour.
                services.AddSingleton<InstallCoordinatorMutex>();

                // 更新下载器：清单读取、安装包下载与校验、退出时的安装交接。
                // Update downloader: manifest reading, installer download and verification, and the install hand-off on exit.
                services.AddSingleton<UpdateManifestClient>();
                services.AddSingleton<UpdatePackageStore>();
                services.AddSingleton<UpdatePackageDownloader>();
                services.AddSingleton<InstalledApplicationProbe>();
                services.AddSingleton<UpdateService>();

                // 导航服务（页面导航，不依赖具体窗口）Navigation service (page navigation, window-independent)
                services.AddSingleton<INavigationService, NavigationService>();

                // === 主窗口（隐藏的宿主窗口）Main Window (invisible host window) ===
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<AudioControlViewModel>();
                services.AddTransient<DynamicIslandWindow>();
                services.AddSingleton<AudioControlFlyoutWindow>();

                // === 设置窗口（从任务栏右键菜单打开）Settings Window (opened from taskbar context menu) ===
                services.AddSingleton<SettingsWindowViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<Func<SettingsWindow>>(sp =>
                    () => sp.GetRequiredService<SettingsWindow>());
                services.AddTransient<TaskbarFullPanelWindow>();
                services.AddSingleton<Func<TaskbarFullPanelWindow>>(sp =>
                    () => sp.GetRequiredService<TaskbarFullPanelWindow>());
                services.AddTransient<TrackChangeNotificationWindow>();
                services.AddSingleton<Func<TrackChangeNotificationWindow>>(sp =>
                    () => sp.GetRequiredService<TrackChangeNotificationWindow>());

                // === 设置页面及其 ViewModel Settings Pages and ViewModels ===
                services.AddSingleton<AppearancePage>();
                services.AddSingleton<AppearanceViewModel>();

                services.AddSingleton<LayoutPage>();
                services.AddSingleton<LayoutViewModel>();

                services.AddSingleton<DisplayModesPage>();
                services.AddSingleton<DisplayModesViewModel>();
                services.AddSingleton<ExtraFeaturesPage>();
                services.AddSingleton<ExtraFeaturesViewModel>();

                services.AddSingleton<InteractionPage>();
                services.AddSingleton<InteractionViewModel>();

                services.AddSingleton<LyricsPage>();
                services.AddSingleton<LyricsViewModel>();

                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                services.AddSingleton<AboutPage>();
                services.AddSingleton<AboutViewModel>();
            }).Build();

        private ApplicationThemeCoordinator? _themeCoordinator;
#if DEBUG
        private DebugLyricsDiagnostics? _debugLyricsDiagnostics;
#endif

        #region FrameWork

        /// <summary>
        /// 全局服务提供者，供应用各处获取依赖服务。
        /// Global service provider for retrieving dependency services throughout the application.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// 应用启动事件：启动 Host 并订阅调试事件。
        /// Application startup event: starts the Host and subscribes to debug events.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            var settingsPersistenceService = Services.GetRequiredService<SettingsPersistenceService>();
            settingsPersistenceService.Initialize();
            var displayMonitorService = Services.GetRequiredService<IDisplayMonitorService>();
            EventHandler? legacyMigrationHandler = null;
            if (settingsPersistenceService.LegacyTaskbarMonitorIndex is { } legacyMonitorIndex)
            {
                legacyMigrationHandler = (_, _) =>
                {
                    if (TryMigrateLegacyTaskbarMonitor(displayMonitorService, legacyMonitorIndex))
                        displayMonitorService.MonitorsChanged -= legacyMigrationHandler;
                };
                displayMonitorService.MonitorsChanged += legacyMigrationHandler;
            }

            displayMonitorService.Refresh();
            if (legacyMigrationHandler is not null &&
                TryMigrateLegacyTaskbarMonitor(
                    displayMonitorService,
                    settingsPersistenceService.LegacyTaskbarMonitorIndex!.Value))
            {
                displayMonitorService.MonitorsChanged -= legacyMigrationHandler;
            }

            // 更新在"这一次启动之前"安装。
            //
            // 退出时安装会把安装程序窗口留在用户刚关掉程序之后，看起来像程序自己又起来了一次；放在这里，
            // 用户看到的顺序是「打开程序 → 安装进度 → 新版本启动」。必须在设置加载之后、宿主启动之前：
            // 设置没加载时 OnExit 的 Flush 会把默认设置写回用户文件，而宿主启动后又会先建出托盘图标与任务栏媒体栏。
            // The update is installed *before* this start.
            //
            // Installing on exit leaves an installer window right after the user closed the application, which looks
            // like the application starting itself again; here the order the user sees is "open the app → install
            // progress → the new version starts". It has to happen after the settings are loaded and before the host
            // starts: without loaded settings, the flush in OnExit would write defaults over the user's file, and once
            // the host has started the tray icon and taskbar bar would already exist.
            var updateService = Services.GetRequiredService<UpdateService>();
            updateService.RestartRequested += (_, _) => Shutdown();
            if (updateService.TryLaunchPendingInstallOnStartup())
            {
                Debug.WriteLine("[App] A pending update is being installed before this start; exiting now.");
                Shutdown();
                return;
            }
            _themeCoordinator = new ApplicationThemeCoordinator(Dispatcher, UpdateAppearanceResources);
            _themeCoordinator.Start();
            _themeCoordinator.Apply(SettingsManager.Current.Appearance);

            // DWM 的强调色变化消息由窗口外观服务统一接收；转交协调器后整套应用级画刷会一起更新。
            // The window appearance service receives DWM's accent-change message; forwarding it makes the coordinator refresh
            // the whole set of application-level brushes at once.
            Services.GetRequiredService<WindowAppearanceService>().SystemColorizationChanged +=
                () => _themeCoordinator?.Apply(SettingsManager.Current.Appearance);
            await _host.StartAsync();

            // 安装协调互斥体必须在 Host 启动后创建：更新链路会在启动安装包之前释放它（见 InstallCoordinatorMutex）。
            // The install-coordination mutex is created after the host starts; the update path releases it before
            // starting the installer (see InstallCoordinatorMutex). A failure here must never affect startup.
            Services.GetRequiredService<InstallCoordinatorMutex>().Acquire();

            // 更新排期在宿主就绪之后启动：它自己带首检延迟，因此不会和媒体会话、任务栏停靠抢启动资源。
            // Update scheduling starts once the host is ready; it carries its own initial delay, so it never competes
            // with media sessions or taskbar docking during startup.
            updateService.Start();

#if DEBUG
            _debugLyricsDiagnostics = new DebugLyricsDiagnostics();
            Services.GetRequiredService<MediaSessionService>().SnapshotChanged += _debugLyricsDiagnostics.OnSnapshotChanged;
#endif
        }

        private static bool TryMigrateLegacyTaskbarMonitor(
            IDisplayMonitorService displayMonitorService,
            int legacyMonitorIndex)
        {
            if (!string.IsNullOrWhiteSpace(SettingsManager.Current.TaskbarTargetMonitorDeviceId))
                return true;

            var monitors = displayMonitorService.GetMonitors();
            if (monitors.Count == 0)
                return false;

            var migratedDeviceId = DisplayTargetPolicy.ResolveLegacyDeviceId(monitors, legacyMonitorIndex);
            if (!string.IsNullOrWhiteSpace(migratedDeviceId))
                SettingsManager.Current.TaskbarTargetMonitorDeviceId = migratedDeviceId;
            return true;
        }

        /// <summary>
        /// 应用退出事件：停止 Host 并释放资源。
        /// Application exit event: stops the Host and disposes resources.
        /// </summary>
        private void OnExit(object sender, ExitEventArgs e)
        {
            // Exit can be requested by both a ContextMenu command and MainWindow.OnClosed.
            // Process the cleanup boundary only once so a re-entrant Shutdown call cannot
            // interrupt disposal halfway through.
            if (Interlocked.Exchange(ref _exitHandled, 1) != 0)
            {
                return;
            }

            // 这两个服务必须在 Host 释放之前取出来。
            // Host.Dispose 同时释放 DI 容器，之后再从 App.Services 解析任何东西都会抛 ObjectDisposedException；
            // 那既会跳过退出时的安装交接，也会把一次正常退出变成一次崩溃（WER 里是 e0434352）。
            // These two services must be resolved before the host is disposed.
            // Host.Dispose also disposes the DI container, and resolving anything from App.Services afterwards
            // throws ObjectDisposedException, which would both skip the install hand-off on exit and turn a normal
            // exit into a crash recorded as e0434352.
            var updateService = Services.GetRequiredService<UpdateService>();
            var installCoordinatorMutex = Services.GetRequiredService<InstallCoordinatorMutex>();

#if DEBUG
            if (_debugLyricsDiagnostics is not null)
                Services.GetRequiredService<MediaSessionService>().SnapshotChanged -= _debugLyricsDiagnostics.OnSnapshotChanged;
            _debugLyricsDiagnostics = null;
#endif
            _themeCoordinator?.Dispose();
            _themeCoordinator = null;

            try
            {
                Services.GetRequiredService<SettingsPersistenceService>().Flush();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Settings flush failed: {exception}");
            }

            // WPF 正在关闭 Dispatcher 时不能从 async void Exit 处理器等待后再恢复到 UI 线程，
            // 否则 Host.Dispose 可能永远不执行，媒体与 Shell 服务会让进程残留。
            // Do not await from an async-void Exit handler while WPF is shutting down its
            // Dispatcher; the continuation may never run and leave hosted resources alive.
            using var shutdown = new CancellationTokenSource(HostShutdownTimeout);
            try
            {
                _host.StopAsync(shutdown.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                Debug.WriteLine("[App] Host shutdown timed out; disposing remaining services.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Host shutdown failed: {exception}");
            }

            // 只有用户明确点过"立即重启并安装"时才在退出边界启动安装程序。
            //
            // 自动更新已经不在退出时发生：它在**下一次启动之前**执行（见 TryLaunchPendingInstallOnStartup），
            // 否则安装程序窗口会出现在用户刚关掉程序之后，看起来像程序自己又起来了一次。仍要放在 Dispose 看门狗
            // 之前：看门狗会在 5 秒后直接结束进程，排在它之后就会与之赛跑。
            // The installer is only started on the exit boundary when the user explicitly clicked "restart and install
            // now".
            //
            // Automatic updates no longer happen on exit: they run *before the next start* (see
            // TryLaunchPendingInstallOnStartup), because otherwise the installer window appears right after the user
            // closed the application and looks like the application starting itself again. It still has to happen
            // before the disposal watchdog, which ends the process after five seconds and would otherwise win the race.
            try
            {
                updateService.TryLaunchPendingInstallOnExit();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Update install hand-off failed: {exception}");
            }

            // 安装程序启动前，交接路径已经释放了协调互斥体；这里只需保证其余情形下它也随进程一起消失。
            // The hand-off already released the coordination mutex before starting the installer; this only makes sure
            // it disappears with the process in every other case.
            installCoordinatorMutex.Dispose();

            // A media-session/native component can occasionally block while disposing
            // after Explorer or a tray Popup has already been torn down. Keep a bounded
            // watchdog so an explicit user exit can never leave AFMediaBar alive forever.
            var disposalCompleted = 0;
            _ = Task.Run(async () =>
            {
                await Task.Delay(HostShutdownTimeout).ConfigureAwait(false);
                if (Volatile.Read(ref disposalCompleted) == 0)
                {
                    Environment.Exit(e.ApplicationExitCode);
                }
            });

            try
            {
                _host.Dispose();
            }
            catch (Exception exception)
            {
                // Cleanup must not cancel the final process-termination step.
                Debug.WriteLine($"[App] Host disposal failed: {exception}");
            }
            finally
            {
                Volatile.Write(ref disposalCompleted, 1);
            }

            // WPF has completed its Exit event, but third-party native media components
            // may own non-background threads. Explicitly terminate after all synchronous
            // cleanup has completed so the process cannot remain in the background.
            Environment.Exit(e.ApplicationExitCode);
        }

        private void UpdateAppearanceResources(AppearanceSettings appearance, ApplicationTheme theme, AccentPalette accent)
        {
            var fontFamily = new FontFamily(appearance.ResolveFontFamilySource(SystemFonts.MessageFontFamily.Source));
            var fontWeight = FontWeight.FromOpenTypeWeight(appearance.FontWeight);
            Resources["AppTextFontFamily"] = fontFamily;
            Resources["ContentControlThemeFontFamily"] = fontFamily;
            Resources["AppTextFontWeight"] = fontWeight;
            Resources["AppTextMediumFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 100, 100, 999));
            Resources["AppTextStrongFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 200, 100, 999));

            // 强调色只在这里发布一次：所有界面（任务栏媒体栏、菜单、完整层、设置页与图示）都引用这几个应用级画刷，
            // 因此系统强调色变化后不需要逐个界面刷新，也不会再出现"设置页是粉色、媒体栏是默认蓝"的分裂。
            // The accent is published exactly once, here: every surface (taskbar bar, menus, full panel, settings pages, and
            // diagrams) references these application-level brushes, so an accent change needs no per-surface refresh and the
            // settings-pink / media-blue split cannot come back.
            Resources["AfAccentBrush"] = CreateFrozenBrush(accent.Accent);
            Resources["AfAccentHoverBrush"] = CreateFrozenBrush(accent.Hover);
            Resources["AfAccentPressedBrush"] = CreateFrozenBrush(accent.Pressed);
            Resources["AfAccentTintBrush"] = CreateFrozenBrush(accent.Tint);
            Resources["AfOnAccentBrush"] = CreateFrozenBrush(accent.OnAccent);

            var dark = theme == ApplicationTheme.Dark || theme == ApplicationTheme.HighContrast && SystemParameters.HighContrast;
            // Context menus always use an opaque Fluent solid surface. Native
            // Mica/Acrylic on Popup HWNDs leaves transparent hit-test regions
            // that can pass clicks through to the window behind the menu.
            var menuColor = dark ? Color.FromRgb(44, 44, 44) : Color.FromRgb(249, 249, 249);
            var menuBrush = new SolidColorBrush(menuColor);
            menuBrush.Freeze();
            Resources["AppMenuBackgroundBrush"] = menuBrush;
            Resources["ContextMenuBackground"] = menuBrush;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 应用未处理异常事件：捕获全局异常以防止应用崩溃。
        /// Application unhandled exception event: catches global exceptions to prevent crashes.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 可在此处添加日志记录或错误上报逻辑
            // Add logging or error reporting logic here
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }

        #endregion
    }
}
