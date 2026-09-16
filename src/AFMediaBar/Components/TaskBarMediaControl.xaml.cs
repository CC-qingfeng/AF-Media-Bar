using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
    /// <summary>播放器表面滚轮及鼠标按键状态。 / Wheel delta and mouse-button state on a player surface.</summary>
    public sealed class PlayerSurfaceWheelEventArgs(
        int delta,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown) : EventArgs
    {
        public int Delta { get; } = delta;
        public bool IsShiftDown { get; } = isShiftDown;
        public bool IsLeftButtonDown { get; } = isLeftButtonDown;
        public bool IsRightButtonDown { get; } = isRightButtonDown;
    }

    /// <summary>
    /// 任务栏媒体控制组件：显示当前播放媒体的信息和封面，响应用户交互。
    /// Taskbar media control component: displays currently playing media info and artwork, responds to user interactions.
    ///
    /// 职责 Responsibilities:
    /// 1. 接收 MediaSnapshot 并更新 UI（标题、艺术家、封面、歌词）
    ///    Receive MediaSnapshot and update UI (title, artist, artwork, lyrics)
    /// 2. 根据任务栏方向（横向/竖向）和大小调整布局
    ///    Adjust layout based on taskbar orientation (horizontal/vertical) and size
    /// 3. 显示当前歌词行（歌词可用时替换标题）
    ///    Display current lyric line (replaces title when lyrics are available)
    /// 4. 处理悬停效果和动画
    ///    Handle hover effects and animations
    ///
    /// ⚠️ 架构约束 Architecture Constraints:
    /// - 此组件只负责 UI 呈现，不包含业务逻辑
    ///   This component is responsible for UI presentation only, no business logic
    /// - 用户操作通过请求事件交给宿主窗口，再由 MainWindowViewModel 执行
    ///   User actions are raised as request events and executed by the host through MainWindowViewModel
    /// - 不直接调用服务，所有数据通过 UpdateSongInfo 方法传入
    ///   Does not call services directly; all data is passed via UpdateSongInfo method
    /// </summary>
    public partial class TaskBarMediaControl : UserControl
    {
        // === 布局渲染引擎 Layout Render Engine ===
        private LayoutRenderEngine? _layoutEngine;
        private WindowMode _currentMode = WindowMode.Taskbar;  // 当前窗口模式 Current window mode

        /// <summary>
        /// 初始化共享媒体控件及其组件级悬停、文本覆盖层和尺寸测量状态。
        /// Initializes the shared media control and its component hover, text-overlay, and size-measurement state.
        /// </summary>
        public TaskBarMediaControl()
        {
            InitializeComponent();

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (_, _) => UpdateTaskbarProgress();
            _hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _hoverOpenTimer.Tick += (_, _) =>
            {
                _hoverOpenTimer.Stop();
                ShowTaskbarHoverLayer();
            };
            _hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hoverCloseTimer.Tick += (_, _) =>
            {
                _hoverCloseTimer.Stop();
                HideTaskbarHoverLayer();
            };
            Loaded += (_, _) => _progressTimer.Start();
            Unloaded += (_, _) =>
            {
                _progressTimer.Stop();
                _hoverOpenTimer.Stop();
                _hoverCloseTimer.Stop();
                StopMarqueeAnimations();
            };

            // 计时器必须先于布局初始化；布局会立即应用已持久化的悬停层开关。
            // Timers must exist before layout initialization, which immediately applies persisted hover settings.
            InitializeLayoutEngine();
        }

        // === 内部状态缓存 Internal State Cache ===
        private string _actualTitle = string.Empty;   // 实际标题（不含歌词）Actual title (without lyrics)
        private string _actualArtist = string.Empty;  // 实际艺术家 Actual artist

        private bool _isPaused;        // 是否暂停 Whether paused
        private bool _isConnected;
        private bool _isVertical;      // 任务栏是否竖向 Whether taskbar is vertical
        private bool _isSmallTaskbar;  // 是否小任务栏 Whether taskbar is small
        private bool _canPlayPause;
        private bool _canSkipPrevious;
        private bool _canSkipNext;
        private string _activeLyric = string.Empty;
        private string _nextLyric = string.Empty;
        private string _translatedLyric = string.Empty;
        private string _secondaryLyric = string.Empty;
        private string _lastSizeFingerprint = string.Empty;
        private string _lastMarqueeFingerprint = string.Empty;
        private int _marqueeUpdateVersion;
        private double _minimumPrimaryLength = 120;
        private readonly DispatcherTimer _progressTimer;
        private readonly DispatcherTimer _hoverOpenTimer;
        private readonly DispatcherTimer _hoverCloseTimer;
        private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
        private bool _isTaskbarHoverVisible;
        private PlayerForegroundDecision? _adaptiveForegroundDecision;
        private IReadOnlyList<QuickLaunchEntry> _quickLaunchEntries = Array.Empty<QuickLaunchEntry>();
        private DateTime _suppressSurfaceClickUntilUtc;
        private const double TaskbarSpectrumWidth = 38;
        private const double TaskbarPerformanceWidth = 74;
        private const double TaskbarTrailingMargin = 4;
        private const double TaskbarCoveredBlurRadius = 4;
        private const double TaskbarCoveredOpacity = 0.32;

        public event EventHandler? TogglePlayPauseRequested;
        public event EventHandler? SkipPreviousRequested;
        public event EventHandler? SkipNextRequested;
        public event EventHandler? ActivateSourceRequested;
        public event EventHandler? OpenFullPanelRequested;
        public event EventHandler? OutputDeviceMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? OutputDeviceWheelRequested;
        public event EventHandler? VolumeMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? VolumeWheelRequested;
        public event EventHandler? QuickLaunchMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? QuickLaunchWheelRequested;
        public event EventHandler? OpenTaskManagerRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? WheelRequested;
        public event EventHandler<MediaBarSizeRequestEventArgs>? DesiredSizeChanged;

        /// <summary>指针进入输出设备按钮，宿主应刷新该按钮提示。 / Pointer entered the output-device button; the host should refresh its tooltip.</summary>
        public event EventHandler? OutputDeviceInfoRequested;

        /// <summary>指针进入音量按钮，宿主应刷新该按钮提示。 / Pointer entered the volume button; the host should refresh its tooltip.</summary>
        public event EventHandler? VolumeInfoRequested;

        /// <summary>当前横向任务栏悬停层和固定组件所需的最小长度。 / Current minimum length required by the horizontal taskbar hover layer and fixed components.</summary>
        public double MinimumPrimaryLength => _minimumPrimaryLength;

        /// <summary>
        /// 返回当前可见文字表面的物理屏幕像素矩形，供宿主采样实际背景。
        /// Returns the physical screen-pixel rectangle of the visible text surface for host background sampling.
        /// </summary>
        public bool TryGetForegroundSampleBounds(out Int32Rect bounds)
        {
            bounds = Int32Rect.Empty;
            if (_isTaskbarHoverVisible)
            {
                return false;
            }

            try
            {
                var regions = new List<Int32Rect>();
                if (_isConnected && TryGetPhysicalBounds(SongInfoSurface, out var mediaBounds))
                    regions.Add(mediaBounds);
                if (TaskbarPerformanceSurface.IsVisible && TryGetPhysicalBounds(TaskbarPerformanceSurface, out var metricBounds))
                    regions.Add(metricBounds);
                if (regions.Count == 0)
                    return false;
                var left = regions.Min(region => region.X);
                var top = regions.Min(region => region.Y);
                var right = regions.Max(region => region.X + region.Width);
                var bottom = regions.Max(region => region.Y + region.Height);
                bounds = new Int32Rect(left, top, right - left, bottom - top);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryGetPhysicalBounds(FrameworkElement element, out Int32Rect bounds)
        {
            bounds = Int32Rect.Empty;
            if (!element.IsVisible || element.ActualWidth <= 1 || element.ActualHeight <= 1 || PresentationSource.FromVisual(element) is null)
                return false;
            var origin = element.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(element);
            if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y)) return false;
            bounds = new Int32Rect((int)Math.Round(origin.X), (int)Math.Round(origin.Y),
                (int)Math.Round(element.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(element.ActualHeight * dpi.DpiScaleY));
            return bounds.Width > 1 && bounds.Height > 1;
        }

        public void ApplyQuickLaunchEntries(IReadOnlyList<QuickLaunchEntry> entries)
        {
            _quickLaunchEntries = entries.ToArray();
        }

        /// <summary>更新音符的快速启动预览提示。 / Updates the note tooltip with the quick-launch preview.</summary>
        public void SetQuickLaunchPreview(QuickLaunchEntry entry) => SongImageBorder.ToolTip = $"快速启动：{entry.DisplayName}";

        /// <summary>
        /// 刷新输出设备按钮提示；文本与托盘图标提示来自同一策略，指针悬停与滚轮预览都经过这里。
        /// Refreshes the output-device button tooltip; the text comes from the same policy as the tray icon tooltip, and
        /// both hover and wheel previews go through it.
        /// </summary>
        public void SetOutputDevicePreview(AudioDeviceOption device) =>
            TaskbarDeviceButton.ToolTip = AudioTooltipPolicy.BuildOutputDevice(device);

        /// <summary>提示当前没有可用输出设备。 / Indicates that no output device is available.</summary>
        public void SetOutputDeviceUnavailable() =>
            TaskbarDeviceButton.ToolTip = AudioTooltipPolicy.BuildOutputDevice(null);

        /// <summary>
        /// 刷新音量按钮提示；滚轮预览的候选值也走这里，因此提示总是先于延迟应用更新。
        /// Refreshes the volume button tooltip; wheel-preview candidates go through it too, so the tooltip always updates
        /// before the deferred apply.
        /// </summary>
        public void SetVolumePreview(int volume)
        {
            // 音量可读但媒体快照暂时没有来源名时给出通用标签，而不是谎报“不可用”。
            // When the volume is readable but the media snapshot has no source name yet, use a generic label instead of
            // reporting the value as unavailable.
            var sourceName = string.IsNullOrWhiteSpace(_snapshot.SourceName) ? "当前媒体" : _snapshot.SourceName;
            TaskbarVolumeButton.ToolTip = AudioTooltipPolicy.BuildMediaVolume(sourceName, volume);
        }

        /// <summary>提示当前媒体没有可匹配音频会话。 / Indicates that the current media has no matching audio session.</summary>
        public void SetVolumeUnavailable() =>
            TaskbarVolumeButton.ToolTip = AudioTooltipPolicy.BuildMediaVolume(null, null);

        /// <summary>返回快速启动菜单的物理屏幕锚点。 / Returns the physical screen anchor for the quick-launch menu.</summary>
        public TrayIconBounds GetQuickLaunchAnchor() => GetScreenBounds(SongImageBorder);

        /// <summary>返回输出设备菜单的物理屏幕锚点。 / Returns the physical screen anchor for the output-device menu.</summary>
        public TrayIconBounds GetOutputDeviceAnchor() => GetScreenBounds(TaskbarDeviceButton);

        /// <summary>返回音量菜单的物理屏幕锚点。 / Returns the physical screen anchor for the volume menu.</summary>
        public TrayIconBounds GetVolumeAnchor() => GetScreenBounds(TaskbarVolumeButton);

        private static TrayIconBounds GetScreenBounds(FrameworkElement element)
        {
            var point = element.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(element);
            return new TrayIconBounds(
                (int)Math.Round(point.X),
                (int)Math.Round(point.Y),
                (int)Math.Round(point.X + element.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(point.Y + element.ActualHeight * dpi.DpiScaleY));
        }

        public void ApplyPerformanceText(string text, bool canOpenTaskManager)
        {
            TaskbarPerformanceText.Text = text;
            TaskbarPerformanceSurface.Cursor = canOpenTaskManager ? Cursors.Hand : Cursors.Arrow;
        }

        /// <summary>
        /// 应用宿主从实际屏幕背景解析出的自动前景决定；null 恢复主题回退。
        /// Applies the automatic foreground decision resolved from the real screen background; null restores theme fallback.
        /// </summary>
        public void ApplyAdaptiveForegroundDecision(PlayerForegroundDecision? decision)
        {
            if (_adaptiveForegroundDecision == decision)
                return;
            _adaptiveForegroundDecision = decision;
            ApplyAppearanceSettings();
        }

        // === 歌词显示状态 Lyrics Display State ===
        // 解析后的行缓存 + 当前行下标，避免每个快照重复解析。
        // Parsed line cache + current line index to avoid re-parsing on every snapshot.
        private readonly LyricLinePresenter _lyricPresenter = new();

        /// <summary>
        /// 初始化布局渲染引擎：将 MainBorder 和 BackgroundImage 传入引擎以便动态调整布局。
        /// Initialize layout render engine: pass MainBorder and BackgroundImage to engine for dynamic layout adjustment.
        /// </summary>
        private void InitializeLayoutEngine()
        {
            _layoutEngine = new LayoutRenderEngine(
                mainBorder: MainBorder,
                contentCanvas: MainCanvas,
                backgroundImage: BackgroundImage,
                artworkBorder: SongImageBorder,
                songInfoPanel: SongInfoStackPanel,
                artworkPlaceholder: SongImagePlaceholder,
                songTitle: SongTitle,
                songArtist: SongArtist,
                songTitleContainer: SongTitleContainer,
                songArtistContainer: SongArtistContainer,
                songLyrics: SongLyrics,
                songLyricsContainer: SongLyricsContainer
            );

            // 应用默认布局（任务栏横向）
            // Apply default layout (taskbar horizontal)
            ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
        }

        /// <summary>
        /// 应用布局：根据窗口模式和方向选择并应用对应的布局配置。
        /// Apply layout: select and apply corresponding layout config based on window mode and orientation.
        /// </summary>
        /// <param name="mode">窗口模式（任务栏/灵动岛）/ Window mode (taskbar/dynamic island)</param>
        /// <param name="orientation">布局方向（横向/竖向）/ Layout orientation (horizontal/vertical)</param>
        public void ApplyLayout(WindowMode mode, LayoutOrientation orientation)
        {
            ApplyLayout(
                mode,
                orientation,
                SettingsManager.Current.LayoutLengthScalePercent,
                SettingsManager.Current.LayoutThicknessScalePercent);
        }

        /// <summary>
        /// 使用宿主解析后的缩放百分比应用布局。
        /// Applies layout with scale percentages resolved by the host.
        /// </summary>
        /// <param name="mode">窗口模式 / Window mode</param>
        /// <param name="orientation">布局方向 / Layout orientation</param>
        /// <param name="lengthScalePercent">主轴长度缩放百分比 / Primary-axis length scale percentage</param>
        /// <param name="thicknessScalePercent">横轴厚度缩放百分比 / Cross-axis thickness scale percentage</param>
        public void ApplyLayout(
            WindowMode mode,
            LayoutOrientation orientation,
            double lengthScalePercent,
            double thicknessScalePercent)
        {
            _currentMode = mode;

            // 从预设中获取布局
            // Get layout from presets
            var layout = LayoutPresets.GetLayout(mode, orientation);

            // 字号设置只作用于任务栏静置层的媒体文字；灵动岛沿用预设字号。
            // The font-size setting only scales taskbar rest-layer media text; the island keeps its preset sizes.
            var mediaFontScale = mode == WindowMode.Taskbar
                ? SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent / 100.0
                : 1.0;

            // 应用布局
            // Apply layout
            _layoutEngine?.ApplyLayout(
                layout,
                lengthScalePercent / 100.0,
                thicknessScalePercent / 100.0,
                mediaFontScale);

            // 更新内部状态标志以保持兼容
            // Update internal state flags to maintain compatibility
            _isVertical = orientation == LayoutOrientation.Vertical;
            ApplyTaskbarExperienceSettings();
            ApplyTaskbarSectionGeometry(MainBorder.Width);
            RaiseDesiredSizeChanged();
        }

        /// <summary>
        /// 获取当前应用的布局配置。
        /// Get currently applied layout configuration.
        /// </summary>
        public LayoutSchema? CurrentLayout => _layoutEngine?.CurrentLayout;

        /// <summary>应用自动计算的主轴长度。/ Applies an auto-calculated primary-axis length.</summary>
        public void ApplyPrimaryLength(double primaryLength)
        {
            _layoutEngine?.ApplyPrimaryLength(primaryLength);
            ApplyTaskbarSectionGeometry(primaryLength);
        }

        /// <summary>
        /// 在宿主更新方向、缩放或字号状态后强制重新发布尺寸请求。
        /// 宿主在这里明确要求重算，因此必须绕过内容指纹去重：布局状态变更期间的早期请求可能因为宿主尚未就绪而被丢弃，
        /// 若被记入指纹，随后这次权威刷新会被去重丢弃，媒体栏就会停在预设画布长度上。
        /// Force-republishes the size request after the host updates orientation, scale, or font-size state. The host is
        /// explicitly asking for a recomputation here, so the content fingerprint must not suppress it: an earlier request
        /// raised while the host was not ready yet can be dropped after being recorded, and this authoritative refresh would
        /// then leave the bar at its preset canvas length.
        /// </summary>
        public void RefreshDesiredSize() => RaiseDesiredSizeChanged(isForcedRefresh: true);

        /// <summary>
        /// 应用横向任务栏的层级和交互设置，不改变原有封面、文字或布局引擎。
        /// Applies horizontal-taskbar layer and interaction settings without replacing the original artwork, text, or layout engine.
        /// </summary>
        public void ApplyTaskbarExperienceSettings()
        {
            var isHorizontalTaskbar = _currentMode == WindowMode.Taskbar && !_isVertical;
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var metrics = TaskbarDensityMetrics.From(experience.Density);
            var progressVisible = _snapshot.Duration > 0;
            var controls = experience.HoverControls;
            var spectrumVisible = isHorizontalTaskbar && experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
            TaskbarPerformanceSurface.Visibility = isHorizontalTaskbar && experience.PerformanceVisible ? Visibility.Visible : Visibility.Collapsed;

            TaskbarSpectrumHoverSurface.Visibility = spectrumVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarSpectrumHoverSurface.IsHitTestVisible = spectrumVisible;
            if (!spectrumVisible)
            {
                AnimateComponentHover(TaskbarSpectrumHoverSurface, false);
                ApplySpectrum(ReadOnlySpan<float>.Empty);
            }
            TaskbarRestProgress.Visibility = isHorizontalTaskbar && progressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarPreviousButton.Visibility = controls.PreviousNextVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarNextButton.Visibility = controls.PreviousNextVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPlayPauseButton.Visibility = controls.PlayPauseVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarTransportButtons.Visibility = controls.PreviousNextVisible || controls.PlayPauseVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarDeviceButton.Visibility = controls.OutputDeviceVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarVolumeButton.Visibility = controls.AudioControlVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarHoverProgress.Visibility = controls.ProgressVisible && progressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarFullPanelHandle.Visibility = experience.FullLayerEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            var directFullPanelHandleVisible = CanShowDirectFullPanelHandle();
            TaskbarDirectFullPanelHandle.Visibility = directFullPanelHandleVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarDirectFullPanelHandle.IsHitTestVisible = directFullPanelHandleVisible;
            if (!directFullPanelHandleVisible)
                AnimateDirectFullPanelHandle(false, immediate: true);
            else
                AnimateDirectFullPanelHandle(
                    SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver,
                    immediate: true);

            foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(TaskbarHoverActions))
            {
                if (ReferenceEquals(button, TaskbarFullPanelHandle))
                    continue;
                button.Width = metrics.ButtonSize;
                button.Height = metrics.ButtonSize;
            }
            TaskbarHoverProgress.Width = metrics.ProgressWidth;
            TaskbarHoverLayer.Height = metrics.HoverLayerHeight;
            ApplyTaskbarSectionGeometry(MainBorder.Width);

            var lyricsAlignment = SettingsManager.Current.LyricsTextAlignment switch
            {
                LyricsTextAlignment.Left => TextAlignment.Left,
                LyricsTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center
            };
            SongMetadataPanel.Orientation = Orientation.Vertical;
            SongArtistContainer.Margin = new Thickness(0, -1.5, 0, 0);
            var metadataAlignment = experience.MediaTextAlignment switch
            {
                TaskbarMediaTextAlignment.Center => TextAlignment.Center,
                TaskbarMediaTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };
            SongTitle.TextAlignment = metadataAlignment;
            SongArtist.TextAlignment = metadataAlignment;
            SongLyrics.TextAlignment = lyricsAlignment;
            SongLyricsSecondary.TextAlignment = lyricsAlignment;
            if (isHorizontalTaskbar && SongMetadataPanel.Visibility == Visibility.Visible)
            {
                if (experience.ContentLayout == TaskbarContentLayout.CompactInline &&
                    !string.IsNullOrEmpty(_actualArtist))
                {
                    SongTitle.Text = string.IsNullOrEmpty(_actualTitle)
                        ? _actualArtist
                        : $"{_actualTitle} · {_actualArtist}";
                    SongArtistContainer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SongTitle.Text = _actualTitle;
                    SongArtistContainer.Visibility = !_isSmallTaskbar && !string.IsNullOrEmpty(_actualArtist)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            else if (!isHorizontalTaskbar)
            {
                SongTitle.Text = _actualTitle;
            }

            if (!isHorizontalTaskbar || !experience.HoverLayerEnabled)
                HideTaskbarHoverLayer(immediate: true);
            if (!isHorizontalTaskbar)
            {
                StopMarqueeAnimations();
                AnimateComponentHover(SongImageHoverOverlay, false);
                AnimateComponentHover(SongInfoHoverOverlay, false);
                AnimateComponentHover(TaskbarSpectrumHoverSurface, false);
            }

            RaiseDesiredSizeChanged();
        }

        /// <summary>
        /// 将统一密度间隔应用到封面、文字和频谱，并让悬停控件只占据文字区域。
        /// Applies one density-controlled gap to artwork, text, and spectrum while constraining hover controls to the text region.
        /// </summary>
        private void ApplyTaskbarSectionGeometry(double primaryLength)
        {
            if (_currentMode != WindowMode.Taskbar || _isVertical || !double.IsFinite(primaryLength))
                return;

            var metrics = TaskbarDensityMetrics.From(SettingsManager.Current.TaskbarExperience.Density);
            var artworkRight = GetTaskbarArtworkRight();
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var spectrumVisible = experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
            var sectionGap = Math.Clamp(SettingsManager.Current.TaskbarExperience.ComponentSpacingDip,
                TaskbarExperienceSettings.MinimumComponentSpacingDip,
                TaskbarExperienceSettings.MaximumComponentSpacingDip);
            var textLeft = artworkRight + (_isConnected ? sectionGap : 0);
            var reservedRight = (spectrumVisible ? sectionGap + TaskbarSpectrumWidth : 0) +
                                (experience.PerformanceVisible ? sectionGap + TaskbarPerformanceWidth : 0) +
                                TaskbarTrailingMargin;
            var textWidth = _isConnected
                ? Math.Max(0, primaryLength - textLeft - reservedRight)
                : 0;
            var textTop = Canvas.GetTop(SongInfoStackPanel);
            if (!double.IsFinite(textTop))
                textTop = 0;

            Canvas.SetLeft(SongInfoStackPanel, textLeft);
            SongInfoStackPanel.Width = textWidth;
            SongInfoSurface.Width = textWidth;
            SongTitleContainer.Width = textWidth;
            SongArtistContainer.Width = textWidth;
            SongLyricsContainer.Width = textWidth;
            SongLyricsSecondaryContainer.Width = textWidth;

            // 任务栏的媒体文字宽度由本节几何唯一决定：布局引擎按布局 schema 写入的 TextBlock 宽度仍包含
            // 频谱与性能组件占用的区间，比真实文字区更宽，会让标题按错误宽度裁剪、在容器边缘被硬切；
            // 悬停路径会通过跑马灯配置重新写回正确宽度，所以这个错误只在设置变更后显现。
            // This section owns the taskbar media-text width: the layout engine writes TextBlock widths from the layout
            // schema, which still covers the spectrum and performance reserve and is wider than the real text area, so the
            // title trims against the wrong width and is hard-cut at the container edge. The hover path rewrites the correct
            // width through the marquee configuration, which is why the defect only shows up after a settings change.
            SongTitle.Width = textWidth;
            SongArtist.Width = textWidth;
            SongLyrics.Width = textWidth;
            SongLyricsSecondary.Width = textWidth;

            Canvas.SetLeft(SongInfoHoverOverlay, textLeft);
            Canvas.SetTop(SongInfoHoverOverlay, textTop);
            SongInfoHoverOverlay.Width = textWidth;
            SongInfoHoverOverlay.Height = SongInfoStackPanel.Height;

            TaskbarRestProgress.Margin = new Thickness(textLeft, 0, reservedRight, 1);
            TaskbarSpectrumHoverSurface.Width = TaskbarSpectrumWidth;
            TaskbarPerformanceSurface.Margin = new Thickness(0, 0, TaskbarTrailingMargin, 0);
            TaskbarSpectrumHoverSurface.Margin = new Thickness(0, 0,
                TaskbarTrailingMargin + (experience.PerformanceVisible ? TaskbarPerformanceWidth + sectionGap : 0), 0);

            HoverRevealHost.Margin = new Thickness(textLeft, 1, 0, 1);
            HoverRevealHost.Height = Math.Max(0, MainBorder.Height - 2);
            TaskbarHoverLayer.Width = textWidth;
            TaskbarDirectFullPanelHandle.Width = textWidth;
            TaskbarDirectFullPanelHandle.Margin = new Thickness(textLeft, 1, 0, 0);
            if (HoverRevealHost.Visibility == Visibility.Visible)
            {
                HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
                HoverRevealHost.Width = textWidth;
                HoverRevealClip.Rect = new Rect(0, 0, textWidth, HoverRevealHost.Height);
            }

            QueueMarqueeUpdate(textWidth);
        }

        private double GetTaskbarArtworkRight()
        {
            var artworkLeft = Canvas.GetLeft(SongImageBorder);
            if (!double.IsFinite(artworkLeft))
                artworkLeft = 0;
            return artworkLeft + Math.Max(0, SongImageBorder.Width);
        }

        /// <summary>当前是否有已连接且正在播放的媒体。/ Indicates whether connected media is currently playing.</summary>
        public bool IsPlaying => _isConnected && !_isPaused;

        /// <summary>
        /// 设置竖向模式：任务栏在屏幕左侧或右侧时调整布局。
        /// Set vertical mode: adjust layout when taskbar is on screen left or right edge.
        /// </summary>
        public void SetVerticalMode(bool isVertical)
        {
            // 使用新的布局系统
            // Use new layout system
            var orientation = isVertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            ApplyLayout(_currentMode, orientation);

            // 兼容性：更新可见性（布局系统已处理尺寸）
            // Compatibility: update visibility (layout system handles sizing)
            SongInfoStackPanel.Visibility = !isVertical && _isConnected
                ? Visibility.Visible
                : Visibility.Collapsed;
            SongInfoStackPanel.IsHitTestVisible = !isVertical && _isConnected;
            SongArtistContainer.Visibility = !_isSmallTaskbar && !isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 设置小任务栏模式：任务栏高度较小时隐藏艺术家信息。
        /// Set small taskbar mode: hide artist info when taskbar height is small.
        /// </summary>
        public void SetSmallTaskbarMode(bool isSmallTaskbar)
        {
            _isSmallTaskbar = isSmallTaskbar;
            SongArtistContainer.Visibility = !isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 应用播放器文字和灵动岛背景设置。
        /// Applies player text and dynamic-island background settings.
        /// </summary>
        public void ApplyAppearanceSettings()
        {
            var appearance = SettingsManager.Current.Appearance.Normalize();
            var appTheme = ApplicationThemeManager.GetAppTheme();
            var isDark = appTheme == ApplicationTheme.Dark;
            if (appTheme == ApplicationTheme.Unknown)
            {
                WindowsThemeDetector.GetWindowsTheme(out var windowsAppTheme, out _);
                isDark = windowsAppTheme == WindowsThemeDetector.ThemeMode.Dark;
            }
            var presentation = PlayerForegroundPolicy.ResolvePresentation(
                appearance.PlayerForegroundMode,
                SystemParameters.HighContrast,
                isDark,
                _adaptiveForegroundDecision);

            Brush foreground;
            if (presentation.UsesSystemColors)
            {
                foreground = SystemColors.WindowTextBrush;
            }
            else
            {
                foreground = new SolidColorBrush(presentation.UsesLightText
                    ? Colors.White
                    : Color.FromRgb(0x1C, 0x1C, 0x1C));
            }

            SongTitle.Foreground = foreground;
            SongLyrics.Foreground = foreground;
            SongLyricsSecondary.Foreground = foreground;
            SongLyricsSecondary.Opacity = SystemParameters.HighContrast ? 1 : 0.68;
            SongArtist.Foreground = foreground;
            TaskbarPerformanceText.Foreground = foreground;
            SongInfoStackPanel.Background = Brushes.Transparent;
            ApplyContrastShadow(presentation.NeedsContrastShadow, presentation.UsesLightText);

            if (_currentMode == WindowMode.Taskbar)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                TopBorder.BorderBrush = Brushes.Transparent;
                BackgroundImage.Visibility = Visibility.Collapsed;
                return;
            }

            if (_currentMode != WindowMode.DynamicIsland)
                return;

            MainBorder.Background = SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent
                ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
                : SystemParameters.HighContrast
                    ? SystemColors.WindowBrush
                    : new SolidColorBrush(isDark
                        ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                        : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        }

        private void ApplyContrastShadow(bool enabled, bool usesLightText)
        {
            Effect? effect = null;
            if (enabled)
            {
                // 阴影只负责对比度，不参与字形：模糊半径保持在 1 DIP，并且必须离开字形轮廓。
                // ShadowDepth 为 0 时模糊副本压在字形正中，会把每条笔画的边缘吃掉，用户看到的就是"文字发虚"；
                // 偏移 1 DIP 后阴影落在轮廓外侧，字形边缘保持干净。
                // The shadow exists for contrast, not for glyph shape: keep the blur radius at 1 DIP but move it off the
                // glyph outline. With ShadowDepth 0 the blurred copy sits centered under the glyphs and eats every stroke
                // edge, which reads as blurry text; a 1 DIP offset keeps the glyph edges clean.
                var shadow = new DropShadowEffect
                {
                    Color = usesLightText ? Colors.Black : Colors.White,
                    BlurRadius = 1,
                    ShadowDepth = 1,
                    Direction = 315,
                    Opacity = 0.85,
                    RenderingBias = RenderingBias.Quality
                };
                shadow.Freeze();
                effect = shadow;
            }

            SongTitle.Effect = effect;
            SongArtist.Effect = effect;
            SongLyrics.Effect = effect;
            SongLyricsSecondary.Effect = effect;
            TaskbarPerformanceText.Effect = effect;
        }


        /// <summary>
        /// 更新歌曲信息：根据快照更新 UI 的所有元素（标题、艺术家、封面、歌词、播放状态）。
        /// Update song info: updates all UI elements based on snapshot (title, artist, artwork, lyrics, playback state).
        ///
        /// 算法 Algorithm:
        /// 1. 断开状态：显示占位符图标，清空所有信息
        ///    Disconnected: show placeholder icon, clear all info
        /// 2. 连接状态：更新标题、艺术家、封面、歌词
        ///    Connected: update title, artist, artwork, lyrics
        /// 3. 封面存在时根据播放/暂停状态显示不同图标
        ///    Show different icon based on play/pause state when artwork exists
        /// 4. 歌词可用时用当前行替换标题显示
        ///    Replace title with current lyric line when lyrics are available
        /// </summary>
        public void UpdateSongInfo(MediaSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (!snapshot.IsConnected)
            {
                // 无媒体播放 - 显示占位符保持媒体栏可见
                // No media playing - show the placeholder text so the media bar stays visible
                Dispatcher.Invoke(() =>
                {
                    _actualTitle = string.Empty;
                    _actualArtist = string.Empty;
                    _isConnected = false;
                    _canPlayPause = false;
                    _canSkipPrevious = false;
                    _canSkipNext = false;
                    _lyricPresenter.Update(null, 0);
                    _activeLyric = string.Empty;
                    _nextLyric = string.Empty;
                    _translatedLyric = string.Empty;
                    _secondaryLyric = string.Empty;

                    SongTitle.Text = _actualTitle;
                    SongLyrics.Text = string.Empty;
                    SongLyricsSecondary.Text = string.Empty;
                    SongMetadataPanel.Visibility = Visibility.Visible;
                    SongLyricsPanel.Visibility = Visibility.Collapsed;
                    SongArtist.Text = _actualArtist;
                    SongInfoStackPanel.Visibility = Visibility.Collapsed;
                    SongInfoStackPanel.IsHitTestVisible = false;
                    SongInfoStackPanel.ToolTip = string.Empty;
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover
                    TaskbarPreviousButton.IsEnabled = false;
                    TaskbarPlayPauseButton.IsEnabled = false;
                    TaskbarNextButton.IsEnabled = false;
                    HideTaskbarHoverLayer(immediate: true);
                    UpdateTaskbarProgress();
                    ApplyTaskbarExperienceSettings();

                    // 任务栏无媒体时保持完全透明；灵动岛保留布局定义的稳定背景。
                    // Keep the disconnected taskbar transparent; preserve the dynamic-island layout background.
                    if (_currentMode == WindowMode.Taskbar)
                    {
                        MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                        MainBorder.Background.Opacity = 0;
                        TopBorder.BorderBrush = Brushes.Transparent;
                    }

                    Visibility = Visibility.Visible;
                    RaiseDesiredSizeChanged(isForcedRefresh: true);
                });
                return;
            }

            _isPaused = !snapshot.IsPlaying;
            _isConnected = true;
            _canPlayPause = snapshot.CanPlayPause;
            _canSkipPrevious = snapshot.CanSkipPrevious;
            _canSkipNext = snapshot.CanSkipNext;

            Dispatcher.Invoke(() =>
            {
                string newTitle = !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : "-";
                string newArtist = !string.IsNullOrWhiteSpace(snapshot.Artist)
                    ? snapshot.Artist
                    : !string.IsNullOrWhiteSpace(snapshot.SourceName) ? snapshot.SourceName : "-";

                // 标题或艺术家变化时触发入场动画
                // Trigger entrance animation when title or artist changes
                if (_actualTitle != newTitle || _actualArtist != newArtist)
                {
                    AnimateEntrance();

                    _actualTitle = newTitle;
                    _actualArtist = newArtist;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                }

                // 歌词可用时标题位置显示当前歌词行（随快照位置推进）
                // Show current lyric line in title slot when lyrics are available (advances with snapshot position)
                UpdateLyricLine(snapshot);

                // 更新工具提示显示完整歌曲信息
                // Update tooltip with full song info
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : string.Empty;
                SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(snapshot.Artist) ? "\n\n" + snapshot.Artist : string.Empty;

                // 根据主色调改变图标颜色（从封面提取）
                // Change icon color based on dominant color (extracted from artwork)
                SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                    ? BitmapHelper.SavedDominantColors.Last()
                    : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
                SongImagePlaceholder.Foreground = brush;

                if (snapshot.Artwork is not null)
                {
                    if (_isPaused)
                    {
                        // show pause icon overlay
                        SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                        SongImagePlaceholder.Visibility = Visibility.Visible;
                        SongImage.Opacity = 0.4;
                    }
                    else
                    {
                        SongImagePlaceholder.Visibility = Visibility.Collapsed;
                        SongImage.Opacity = 1;
                    }

                    SongImage.ImageSource = snapshot.Artwork;
                    BackgroundImage.Source = snapshot.Artwork;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
                }
                else
                {
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                }

                ApplyLyricPresentation();
                SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
                SongInfoStackPanel.IsHitTestVisible = !_isVertical;
                // 任务栏主体保持透明；灵动岛继续沿用布局引擎已有背景行为。
                // Keep the taskbar body transparent; the island retains its existing layout-engine background behavior.
                BackgroundImage.Visibility = Visibility.Collapsed;

                TaskbarPreviousButton.IsEnabled = _canSkipPrevious;
                TaskbarPlayPauseButton.IsEnabled = _canPlayPause;
                TaskbarNextButton.IsEnabled = _canSkipNext;
                TaskbarPlayPauseIcon.Symbol = _isPaused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
                UpdateTaskbarProgress();
                ApplyTaskbarExperienceSettings();

                Visibility = Visibility.Visible;
                RaiseDesiredSizeChanged();
            });
        }

        /// <summary>
        /// 用快照中的歌词和位置更新标题区域的当前行；无歌词时恢复标题。
        /// Shows the active lyric line in the title slot from the snapshot; restores the title when lyrics are absent.
        /// </summary>
        private void UpdateLyricLine(MediaSnapshot snapshot)
        {
            var update = _lyricPresenter.Update(snapshot.Lyrics, snapshot.Position);
            _activeLyric = update.Text;
            _nextLyric = update.NextText;
            _translatedLyric = update.TranslationText;
            SongTitle.Text = _actualTitle;
        }

        private void ApplyLyricPresentation()
        {
            var settings = SettingsManager.Current;
            var showLyrics = settings.LyricsEnabled && !string.IsNullOrEmpty(_activeLyric);
            _secondaryLyric = settings.LyricsSecondaryLineMode == LyricsSecondaryLineMode.Translation
                ? _translatedLyric
                : _nextLyric;
            var showSecondary = showLyrics &&
                                settings.TwoLineLyricsEnabled &&
                                !string.IsNullOrEmpty(_secondaryLyric);

            SongMetadataPanel.Visibility = showLyrics ? Visibility.Collapsed : Visibility.Visible;
            SongLyricsPanel.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            SongLyrics.Text = showLyrics ? _activeLyric : string.Empty;
            SongLyricsSecondary.Text = showSecondary ? _secondaryLyric : string.Empty;
            SongLyricsSecondaryContainer.Visibility = showSecondary ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRowSpan(SongLyricsContainer, showSecondary ? 1 : 2);
            SongLyricsContainer.VerticalAlignment = showSecondary
                ? VerticalAlignment.Stretch
                : VerticalAlignment.Center;
        }

        /// <summary>根据当前可见文本发布自动尺寸请求。/ Raises an auto-size request for the visible text.</summary>
        /// <param name="isForcedRefresh">是否绕过内容指纹去重。/ Whether the content-fingerprint dedupe is bypassed.</param>
        private void RaiseDesiredSizeChanged(bool isForcedRefresh = false)
        {
            if (_layoutEngine?.CurrentOrientation is not { } orientation)
                return;

            var lyricsVisible = SongLyricsPanel.Visibility == Visibility.Visible;
            var visibleText = lyricsVisible ? _activeLyric : SongTitle.Text;
            var secondaryText = lyricsVisible && SongLyricsSecondaryContainer.Visibility == Visibility.Visible
                ? _secondaryLyric
                : string.Empty;
            var artist = !lyricsVisible && SongArtistContainer.Visibility == Visibility.Visible ? _actualArtist : string.Empty;
            // Spectrum tuning and metric selection never change their reserved widths. Keeping
            // those values (or play/pause) in the fingerprint causes redundant host size
            // animations and visibly nudges title/artist/lyrics while sliders are adjusted.
            var fingerprint = $"{orientation}|{visibleText}|{secondaryText}|{artist}|{SongTitle.FontSize:0.##}|{SongArtist.FontSize:0.##}|{SettingsManager.Current.LayoutLengthScalePercent:0.##}|{SettingsManager.Current.LayoutThicknessScalePercent:0.##}|{SettingsManager.Current.LyricsEnabled}|{SettingsManager.Current.TwoLineLyricsEnabled}|{SettingsManager.Current.LyricsSecondaryLineMode}|{SettingsManager.Current.TaskbarExperience}|{_snapshot.IsConnected}|{_snapshot.Duration > 0}";

            // 没有订阅者的请求不会被任何宿主消费，因此不能记入指纹；否则订阅后的首次请求会被去重丢弃，
            // 媒体栏在上一次媒体连接之前一直停留在预设长度。
            // A request raised without a subscriber is never consumed, so it must not be recorded: otherwise the first
            // request after the host subscribes is dropped by the dedupe and the bar keeps its preset length until the next
            // media connection.
            if (DesiredSizeChanged is null)
                return;

            if (!isForcedRefresh && fingerprint == _lastSizeFingerprint)
                return;

            _lastSizeFingerprint = fingerprint;
            var textWidth = Math.Max(
                Math.Max(MeasureTextWidth(visibleText, lyricsVisible ? SongLyrics : SongTitle),
                    MeasureTextWidth(secondaryText, SongLyricsSecondary)),
                MeasureTextWidth(artist, SongArtist));
            var preset = LayoutPresets.GetLayout(_currentMode, orientation);
            var request = LayoutSizeCalculator.Calculate(
                preset,
                SettingsManager.Current.LayoutLengthScalePercent / 100.0,
                SettingsManager.Current.LayoutThicknessScalePercent / 100.0,
                textWidth,
                double.PositiveInfinity,
                fingerprint,
                isForcedRefresh);
            if (_currentMode == WindowMode.Taskbar && orientation == LayoutOrientation.Horizontal)
            {
                var experience = SettingsManager.Current.TaskbarExperience.Normalize();
                var spectrumVisible = experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
                var progressVisible = _snapshot.Duration > 0;
                var contentWidth = TaskbarExperiencePolicy.CalculateWidth(
                    textWidth,
                    GetTaskbarArtworkRight(),
                    TaskbarSpectrumWidth,
                    TaskbarTrailingMargin,
                    _snapshot.IsConnected,
                    spectrumVisible,
                    experience.HoverControls.PlayPauseVisible || experience.HoverControls.PreviousNextVisible,
                    experience.HoverLayerEnabled,
                    progressVisible,
                    experience.Density,
                    double.PositiveInfinity,
                    experience.ComponentSpacingDip,
                    performanceVisible: experience.PerformanceVisible,
                    performanceWidth: TaskbarPerformanceWidth,
                    hoverControls: experience.HoverControls);
                _minimumPrimaryLength = TaskbarExperiencePolicy.CalculateWidth(
                    0,
                    GetTaskbarArtworkRight(),
                    TaskbarSpectrumWidth,
                    TaskbarTrailingMargin,
                    _snapshot.IsConnected,
                    spectrumVisible,
                    experience.HoverControls.PlayPauseVisible || experience.HoverControls.PreviousNextVisible,
                    experience.HoverLayerEnabled,
                    progressVisible,
                    experience.Density,
                    double.PositiveInfinity,
                    experience.ComponentSpacingDip,
                    performanceVisible: experience.PerformanceVisible,
                    performanceWidth: TaskbarPerformanceWidth,
                    hoverControls: experience.HoverControls);
                request = request with
                {
                    Width = _snapshot.IsConnected
                        ? TaskbarExperiencePolicy.ResolvePrimaryLength(
                            contentWidth,
                            _minimumPrimaryLength,
                            double.PositiveInfinity,
                            experience.LengthMode,
                            experience.FixedLengthDip)
                        : contentWidth
                };
            }
            DesiredSizeChanged?.Invoke(this, new MediaBarSizeRequestEventArgs(request));
        }

        private void SongImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                DateTime.UtcNow < _suppressSurfaceClickUntilUtc)
                return;

            if (!_isConnected)
            {
                if (_currentMode == WindowMode.Taskbar && !_isVertical)
                {
                    QuickLaunchMenuRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                return;
            }

            var action = _currentMode == WindowMode.Taskbar
                ? PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork: true)
                : PlayerClickAction.TogglePlayPause;
            if (action == PlayerClickAction.TogglePlayPause)
            {
                if (!_canPlayPause) return;
                TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ActivateSourceRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        private void InteractionSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Preview events tunnel through this parent before the button handlers.
            // Leave both audio buttons in control so their wheel input never becomes a media gesture.
            if (TaskbarDeviceButton.IsMouseOver || TaskbarVolumeButton.IsMouseOver)
                return;

            if (!_isConnected)
            {
                if (_currentMode == WindowMode.Taskbar && !_isVertical && SongImageBorder.IsMouseOver && _quickLaunchEntries.Count > 0)
                {
                    QuickLaunchWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
                    e.Handled = true;
                }
                return;
            }

            if (_currentMode == WindowMode.Taskbar)
            {
                var leftDown = Mouse.LeftButton == MouseButtonState.Pressed;
                var rightDown = Mouse.RightButton == MouseButtonState.Pressed;
                var shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                var modifier = SettingsManager.Current.Interaction.Normalize().Modifier;
                if ((modifier == InteractionModifier.LeftMouseButton && leftDown) ||
                    (modifier == InteractionModifier.RightMouseButton && rightDown))
                    _suppressSurfaceClickUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
                WheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, shiftDown, leftDown, rightDown));
                e.Handled = true;
            }
            else if (e.Delta > 0 && _canSkipPrevious)
            {
                SkipPreviousRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Delta < 0 && _canSkipNext)
            {
                SkipNextRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void TaskbarPreviousButton_Click(object sender, RoutedEventArgs e) =>
            SkipPreviousRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarPlayPauseButton_Click(object sender, RoutedEventArgs e) =>
            TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarNextButton_Click(object sender, RoutedEventArgs e) =>
            SkipNextRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarDeviceButton_Click(object sender, RoutedEventArgs e) =>
            OutputDeviceMenuRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_Click(object sender, RoutedEventArgs e) =>
            VolumeMenuRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarDeviceButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            OutputDeviceWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
            e.Handled = true;
        }

        private void TaskbarDeviceButton_MouseEnter(object sender, MouseEventArgs e) =>
            OutputDeviceInfoRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_MouseEnter(object sender, MouseEventArgs e) =>
            VolumeInfoRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            VolumeWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
            e.Handled = true;
        }

        private void TaskbarPerformanceSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick && e.ChangedButton == MouseButton.Left)
            {
                OpenTaskManagerRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void TaskbarFullPanelHandle_Click(object sender, RoutedEventArgs e) =>
            OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

        private void UpdateTaskbarProgress()
        {
            var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
            var hasDuration = _snapshot.Duration > 0;
            TaskbarRestProgress.Maximum = Math.Max(1, _snapshot.Duration);
            TaskbarRestProgress.Value = position;
            TaskbarHoverProgress.Maximum = Math.Max(1, _snapshot.Duration);
            TaskbarHoverProgress.Value = position;
            if (_currentMode == WindowMode.Taskbar && !_isVertical)
            {
                TaskbarRestProgress.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
                TaskbarHoverProgress.Visibility = hasDuration && SettingsManager.Current.TaskbarExperience.HoverControls.ProgressVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed)
                    yield return typed;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void SongTitleContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected || e.ChangedButton != MouseButton.Left || DateTime.UtcNow < _suppressSurfaceClickUntilUtc)
                return;

            var action = _currentMode == WindowMode.Taskbar
                ? PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork: false)
                : PlayerClickAction.ActivateSource;
            if (action == PlayerClickAction.TogglePlayPause)
            {
                if (!_canPlayPause) return;
                TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ActivateSourceRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }
}
