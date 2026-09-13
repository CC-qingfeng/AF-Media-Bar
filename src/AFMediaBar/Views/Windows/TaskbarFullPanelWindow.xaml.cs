using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>任务栏外的完整媒体、音频与性能面板。 / Full media, audio, and performance panel outside the taskbar.</summary>
public partial class TaskbarFullPanelWindow : FluentWindow
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly SystemMetricsService _metricsService;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly DispatcherTimer _timer;
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private ApplicationVolumeSnapshot? _currentVolume;
    private bool _isLoadingDevices;
    private bool _isSeeking;
    private bool _audioControlsVisible;
    private bool _performanceVisible;
    private bool _isClosing;
    private Rect? _anchor;

    public TaskbarFullPanelWindow(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService,
        SystemMetricsService metricsService,
        WindowAppearanceService appearanceService,
        IDisplayMonitorService displayMonitorService)
    {
        InitializeComponent();
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
        _metricsService = metricsService;
        _displayMonitorService = displayMonitorService;
        appearanceService.Attach(this);
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged += OnTaskbarExperienceSettingsChanged;
        Closed += OnClosed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
        ApplyFullPanelSettings();
    }

    public void ToggleNear(Rect anchor)
    {
        if (_isClosing)
            return;

        if (IsVisible)
        {
            RequestClose();
            return;
        }

        _anchor = anchor;
        Show();
        UpdateLayout();
        PositionNear(anchor);
        Activate();
    }

    /// <summary>幂等关闭完整层，避免失活事件重入窗口关闭。 / Closes the full panel idempotently without deactivation reentrancy.</summary>
    internal void RequestClose()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        Close();
    }

    private void PositionNear(Rect anchor)
    {
        var monitor = _displayMonitorService.ResolveFixedMonitor(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        var scaleX = Math.Max(1d / 96d, (monitor?.DpiX ?? 96) / 96d);
        var scaleY = Math.Max(1d / 96d, (monitor?.DpiY ?? 96) / 96d);
        var work = monitor is null || monitor.WorkArea.IsEmpty
            ? SystemParameters.WorkArea
            : new Rect(
                monitor.WorkArea.Left / scaleX,
                monitor.WorkArea.Top / scaleY,
                monitor.WorkArea.Width / scaleX,
                monitor.WorkArea.Height / scaleY);
        Left = Math.Clamp(anchor.Left + (anchor.Width - ActualWidth) / 2, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        var above = anchor.Top - ActualHeight - 8;
        var below = anchor.Bottom + 8;
        var desiredTop = above >= work.Top ? above : below;
        Top = Math.Clamp(desiredTop, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot) => Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "暂无媒体" : snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.SourceName : snapshot.Artist;
        SourceText.Text = snapshot.SourceName;
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.IsEnabled = snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.CanSkipNext;
        PlayButton.IsEnabled = snapshot.CanPlayPause;
        RepeatButton.IsEnabled = snapshot.CanChangeRepeat;
        RepeatButton.ToolTip = snapshot.RepeatMode switch
        {
            MediaRepeatMode.One => "单曲循环",
            MediaRepeatMode.All => "列表循环",
            MediaRepeatMode.Off => "循环关闭",
            _ => "循环不可用"
        };
        PlayIcon.Symbol = snapshot.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        ProgressSlider.IsEnabled = snapshot.CanSeek;
        ProgressSlider.Maximum = Math.Max(1, snapshot.Duration);
        DurationText.Text = FormatTime(snapshot.Duration);
        UpdateProgress();
    }

    private void OnTaskbarExperienceSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ApplyFullPanelSettings);

    private void ApplyFullPanelSettings()
    {
        var settings = SettingsManager.Current.TaskbarExperience.FullPanel.Normalize();
        var audioBecameVisible = !_audioControlsVisible && settings.AudioControlsVisible;
        _audioControlsVisible = settings.AudioControlsVisible;
        _performanceVisible = settings.PerformanceVisible;

        MediaInfoSection.Visibility = settings.MediaInfoVisible ? Visibility.Visible : Visibility.Collapsed;
        MediaControlsSection.Visibility = settings.MediaControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        AudioControlsSection.Visibility = settings.AudioControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        PerformanceSection.Visibility = settings.PerformanceVisible ? Visibility.Visible : Visibility.Collapsed;

        if (audioBecameVisible)
            _ = RefreshAudioAsync();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            UpdateLayout();
            if (IsVisible && _anchor is Rect anchor)
                PositionNear(anchor);
        }));
    }

    private async Task RefreshAudioAsync()
    {
        try
        {
            _isLoadingDevices = true;
            var devices = await _audioInteractionService.GetOutputDevicesAsync();
            DeviceCombo.ItemsSource = devices;
            DeviceCombo.SelectedItem = devices.FirstOrDefault(device => device.IsDefault);
            _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
            VolumeSlider.IsEnabled = _currentVolume is not null;
            VolumeSlider.Value = _currentVolume?.VolumePercent ?? 0;
            VolumeText.Text = _currentVolume is null ? "不可用" : $"{_currentVolume.VolumePercent}%";
        }
        finally
        {
            _isLoadingDevices = false;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (MediaControlsSection.Visibility == Visibility.Visible)
            UpdateProgress();
        if (_performanceVisible)
        {
            var metrics = _metricsService.Sample();
            RamMetric.Text = $"{metrics.SystemMemoryPercent}%";
            CpuMetric.Text = metrics.SystemCpuPercent is int cpu ? $"{cpu}%" : "—";
            GpuMetric.Text = metrics.SystemGpuPercent is int gpu ? $"{gpu}%" : "—";
            ProcessMetric.Text = $"{metrics.ProcessMemoryMegabytes} MB";
        }
    }

    private void UpdateProgress()
    {
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        if (!_isSeeking)
            ProgressSlider.Value = position;
        PositionText.Text = FormatTime(position);
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipPreviousAsync();
    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.TogglePlayPauseAsync();
    private async void NextButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipNextAsync();
    private async void RepeatButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.CycleRepeatModeAsync();

    private async void ArtworkBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            SettingsManager.Current.Interaction.Mode is MediaInteractionMode.Hybrid or MediaInteractionMode.Gestures)
        {
            await _mediaSessionService.TogglePlayPauseAsync();
            e.Handled = true;
        }
    }

    private void ProgressSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;
    private async void ProgressSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_snapshot.CanSeek)
            await _mediaSessionService.SeekAsync(ProgressSlider.Value);
    }

    private async void DeviceCombo_DropDownOpened(object sender, EventArgs e) => await RefreshAudioAsync();
    private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingDevices && DeviceCombo.SelectedItem is AudioDeviceOption device)
            await _audioInteractionService.SetOutputDeviceAsync(device);
    }

    private async void VolumeSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentVolume is null)
            return;
        var value = (int)Math.Round(VolumeSlider.Value);
        await Task.Run(() => _audioInteractionService.SetApplicationVolume(_currentVolume.ProcessName, value));
        VolumeText.Text = $"{value}%";
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!DeviceCombo.IsDropDownOpen)
            RequestClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            RequestClose();
    }

    /// <summary>标记窗口已进入关闭流程，覆盖所有外部关闭入口。 / Marks the window as closing for every external close path.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
        if (e.Cancel)
            _isClosing = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _mediaSessionService.SnapshotChanged -= OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged -= OnTaskbarExperienceSettingsChanged;
        Closed -= OnClosed;
    }
}
