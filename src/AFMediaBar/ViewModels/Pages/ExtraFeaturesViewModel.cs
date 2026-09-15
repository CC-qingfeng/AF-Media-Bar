using System.Collections.ObjectModel;
using System.IO;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>额外功能页的来源、快捷启动、频谱、性能和通知设置。 / Settings for sources, launchers, spectrum, metrics, and notifications.</summary>
public partial class ExtraFeaturesViewModel : ObservableObject
{
    private readonly MediaSessionService _mediaSessions;
    private readonly IDisplayMonitorService _displayMonitorService;
    private bool _isRefreshing;

    public ObservableCollection<MediaSourceSettingItem> Sources { get; } = [];
    public ObservableCollection<QuickLaunchEntry> QuickLaunchEntries { get; } = [];
    public IReadOnlyList<DisplayMonitorOption> MonitorOptions { get; private set; } = [];

    [ObservableProperty] private string _statusText = string.Empty;

    public ExtraFeaturesViewModel(MediaSessionService mediaSessions, IDisplayMonitorService displayMonitorService)
    {
        _mediaSessions = mediaSessions;
        _displayMonitorService = displayMonitorService;
        _mediaSessions.DiscoveredSourcesChanged += OnDiscoveredSourcesChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;
        RefreshAll();
    }

    public bool SmtcFilterEnabled
    {
        get => SettingsManager.Current.SmtcSourceFilter.Enabled;
        set
        {
            if (_isRefreshing || value == SmtcFilterEnabled) return;
            SettingsManager.SetSmtcSourceFilterSettings(SettingsManager.Current.SmtcSourceFilter with { Enabled = value });
            OnPropertyChanged();
        }
    }

    public int SpectrumBandCount
    {
        get => SettingsManager.Current.SpectrumComponent.BandCount;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { BandCount = value }); OnPropertyChanged(); }
    }

    public int SpectrumRefreshRateHz
    {
        get => SettingsManager.Current.SpectrumComponent.RefreshRateHz;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { RefreshRateHz = value }); OnPropertyChanged(); }
    }

    public int SpectrumSensitivityPercent
    {
        get => SettingsManager.Current.SpectrumComponent.SensitivityPercent;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { SensitivityPercent = value }); OnPropertyChanged(); }
    }

    public int PerformanceRefreshIntervalMilliseconds
    {
        get => SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds;
        set { SettingsManager.SetPerformanceComponentSettings(SettingsManager.Current.PerformanceComponent with { RefreshIntervalMilliseconds = value }); OnPropertyChanged(); }
    }

    public bool OpenTaskManagerOnMetricsClick
    {
        get => SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick;
        set { SettingsManager.SetPerformanceComponentSettings(SettingsManager.Current.PerformanceComponent with { OpenTaskManagerOnClick = value }); OnPropertyChanged(); }
    }

    public bool ShowSystemMemory { get => HasMetric(MetricKind.SystemMemory); set => SetMetric(MetricKind.SystemMemory, value); }
    public bool ShowSystemCpu { get => HasMetric(MetricKind.SystemCpu); set => SetMetric(MetricKind.SystemCpu, value); }
    public bool ShowSystemGpu { get => HasMetric(MetricKind.SystemGpu); set => SetMetric(MetricKind.SystemGpu, value); }
    public bool ShowProcessMemory { get => HasMetric(MetricKind.ProcessMemory); set => SetMetric(MetricKind.ProcessMemory, value); }

    public bool TrackChangeNotificationEnabled { get => Notification.Enabled; set => UpdateNotification(Notification with { Enabled = value }); }
    public bool ShowTrackChangeNotificationWhenFullscreen { get => Notification.ShowWhenFullscreen; set => UpdateNotification(Notification with { ShowWhenFullscreen = value }); }
    public int TrackChangeNotificationDurationSeconds { get => Notification.DurationMilliseconds / 1000; set => UpdateNotification(Notification with { DurationMilliseconds = value * 1000 }); }
    public TrackChangeNotificationPosition TrackChangeNotificationPosition { get => Notification.Position; set => UpdateNotification(Notification with { Position = value }); }
    public NotificationTargetMode TrackChangeNotificationTargetMode { get => Notification.TargetMode; set => UpdateNotification(Notification with { TargetMode = value }); }
    public string? TrackChangeNotificationFixedMonitorDeviceId { get => Notification.FixedMonitorDeviceId ?? _displayMonitorService.ResolveFixedMonitor(null)?.DeviceId; set => UpdateNotification(Notification with { FixedMonitorDeviceId = value }); }
    public bool CanSelectTrackChangeNotificationMonitor => TrackChangeNotificationEnabled && TrackChangeNotificationTargetMode == NotificationTargetMode.Fixed;

    private TrackChangeNotificationSettings Notification => SettingsManager.Current.TrackChangeNotification;

    [RelayCommand]
    private void AddDetectedSource(MediaSourceSettingItem? item)
    {
        if (item?.Descriptor is not { CanQuickLaunch: true } descriptor || descriptor.LaunchKind is not { } kind || descriptor.LaunchTarget is null)
        {
            StatusText = "无法解析该来源的安全启动方式，请浏览 EXE 或 LNK。";
            return;
        }
        AddQuickLaunch(new QuickLaunchEntry(Guid.NewGuid().ToString("N"), descriptor.DisplayName, kind, descriptor.LaunchTarget, descriptor.SourceId));
    }

    [RelayCommand]
    private void RemoveQuickLaunch(QuickLaunchEntry? entry)
    {
        if (entry is null) return;
        SaveQuickLaunch(QuickLaunchEntries.Where(candidate => candidate.Id != entry.Id));
    }

    [RelayCommand]
    private void MoveQuickLaunchUp(QuickLaunchEntry? entry) => Move(entry, -1);

    [RelayCommand]
    private void MoveQuickLaunchDown(QuickLaunchEntry? entry) => Move(entry, 1);

    public void AddQuickLaunchFile(string path)
    {
        var extension = Path.GetExtension(path);
        var kind = string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            ? QuickLaunchTargetKind.Executable
            : string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                ? QuickLaunchTargetKind.Shortcut
                : (QuickLaunchTargetKind?)null;
        if (kind is null || !File.Exists(path))
        {
            StatusText = "只能添加现有的 EXE 或 LNK 文件。";
            return;
        }
        AddQuickLaunch(new QuickLaunchEntry(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(path), kind.Value, Path.GetFullPath(path)));
    }

    public void ResetExtraFeatures() => SettingsManager.ResetExtraFeatures();

    private void AddQuickLaunch(QuickLaunchEntry entry)
    {
        var before = SettingsManager.Current.QuickLaunch.Entries?.Count ?? 0;
        SaveQuickLaunch(QuickLaunchEntries.Append(entry));
        StatusText = (SettingsManager.Current.QuickLaunch.Entries?.Count ?? 0) == before ? "该应用已在快速启动列表中。" : "已添加到快速启动。";
    }

    private void Move(QuickLaunchEntry? entry, int offset)
    {
        if (entry is null) return;
        var items = QuickLaunchEntries.ToList();
        var index = items.FindIndex(candidate => candidate.Id == entry.Id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= items.Count) return;
        (items[index], items[target]) = (items[target], items[index]);
        SaveQuickLaunch(items);
    }

    private void SaveQuickLaunch(IEnumerable<QuickLaunchEntry> entries) =>
        SettingsManager.SetQuickLaunchSettings(new QuickLaunchSettings(entries.ToArray()));

    private bool HasMetric(MetricKind metric) => SettingsManager.Current.PerformanceComponent.Metrics!.Contains(metric);

    private void SetMetric(MetricKind metric, bool enabled)
    {
        var metrics = SettingsManager.Current.PerformanceComponent.Metrics!.ToList();
        if (enabled && !metrics.Contains(metric)) metrics.Add(metric);
        if (!enabled && metrics.Count > 1) metrics.Remove(metric);
        SettingsManager.SetPerformanceComponentSettings(SettingsManager.Current.PerformanceComponent with { Metrics = metrics });
        RaiseMetricProperties();
    }

    private void UpdateNotification(TrackChangeNotificationSettings settings)
    {
        SettingsManager.SetTrackChangeNotificationSettings(settings);
        OnPropertyChanged(nameof(TrackChangeNotificationEnabled));
        OnPropertyChanged(nameof(ShowTrackChangeNotificationWhenFullscreen));
        OnPropertyChanged(nameof(TrackChangeNotificationDurationSeconds));
        OnPropertyChanged(nameof(TrackChangeNotificationPosition));
        OnPropertyChanged(nameof(TrackChangeNotificationTargetMode));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
        OnPropertyChanged(nameof(CanSelectTrackChangeNotificationMonitor));
    }

    private void OnDiscoveredSourcesChanged(IReadOnlyList<MediaSourceDescriptor> sources) => RefreshSources();
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => RefreshAll();
    private void OnMonitorsChanged(object? sender, EventArgs e) => RefreshMonitors();

    private void RefreshAll()
    {
        _isRefreshing = true;
        try
        {
            RefreshSources();
            QuickLaunchEntries.Clear();
            foreach (var entry in SettingsManager.Current.QuickLaunch.Entries ?? []) QuickLaunchEntries.Add(entry);
            RefreshMonitors();
            OnPropertyChanged(nameof(SmtcFilterEnabled));
            OnPropertyChanged(nameof(SpectrumBandCount));
            OnPropertyChanged(nameof(SpectrumRefreshRateHz));
            OnPropertyChanged(nameof(SpectrumSensitivityPercent));
            OnPropertyChanged(nameof(PerformanceRefreshIntervalMilliseconds));
            OnPropertyChanged(nameof(OpenTaskManagerOnMetricsClick));
            RaiseMetricProperties();
            UpdateNotification(Notification);
        }
        finally { _isRefreshing = false; }
    }

    private void RefreshSources()
    {
        var allowed = SettingsManager.Current.SmtcSourceFilter.AllowedSourceIds ?? [];
        var descriptors = _mediaSessions.CurrentDiscoveredSources.ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceId in allowed)
            descriptors.TryAdd(sourceId, new MediaSourceDescriptor(sourceId, MediaSourceNameFormatter.GetDisplayName(sourceId, sourceId), null, null));

        Sources.Clear();
        foreach (var descriptor in descriptors.Values.OrderBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MediaSourceSettingItem(descriptor, allowed.Contains(descriptor.SourceId, StringComparer.OrdinalIgnoreCase));
            item.AllowedChanged += OnSourceAllowedChanged;
            Sources.Add(item);
        }
    }

    private void OnSourceAllowedChanged(MediaSourceSettingItem item)
    {
        var allowed = Sources.Where(source => source.IsAllowed).Select(source => source.Descriptor.SourceId).ToArray();
        SettingsManager.SetSmtcSourceFilterSettings(SettingsManager.Current.SmtcSourceFilter with { AllowedSourceIds = allowed });
    }

    private void RefreshMonitors()
    {
        MonitorOptions = _displayMonitorService.GetMonitors().Select((monitor, index) => new DisplayMonitorOption(
            monitor.DeviceId, $"{index + 1}. {monitor.DeviceName}{(monitor.IsPrimary ? "（主显示器）" : string.Empty)}", monitor.IsPrimary)).ToArray();
        OnPropertyChanged(nameof(MonitorOptions));
    }

    private void RaiseMetricProperties()
    {
        OnPropertyChanged(nameof(ShowSystemMemory));
        OnPropertyChanged(nameof(ShowSystemCpu));
        OnPropertyChanged(nameof(ShowSystemGpu));
        OnPropertyChanged(nameof(ShowProcessMemory));
    }
}

/// <summary>设置页中的可选媒体来源。 / Selectable media source shown on the settings page.</summary>
public partial class MediaSourceSettingItem : ObservableObject
{
    public MediaSourceDescriptor Descriptor { get; }
    [ObservableProperty] private bool _isAllowed;
    public event Action<MediaSourceSettingItem>? AllowedChanged;

    public MediaSourceSettingItem(MediaSourceDescriptor descriptor, bool isAllowed)
    {
        Descriptor = descriptor;
        _isAllowed = isAllowed;
    }

    partial void OnIsAllowedChanged(bool value) => AllowedChanged?.Invoke(this);
}
