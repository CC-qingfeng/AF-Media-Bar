using System.Windows.Threading;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>在多个可见表面之间合并性能采样计时和 GPU 需求。 / Coalesces metric sampling and GPU demand across visible surfaces.</summary>
public sealed class SystemMetricsMonitorService : IDisposable
{
    private readonly SystemMetricsService _sampler;
    private readonly DispatcherTimer _timer = new();
    private readonly List<Subscription> _subscriptions = [];
    private bool _disposed;

    public SystemMetricsMonitorService(SystemMetricsService sampler)
    {
        _sampler = sampler;
        _timer.Tick += OnTick;
    }

    public IDisposable Subscribe(IReadOnlyCollection<MetricKind> metrics, TimeSpan interval, Action<SystemMetricsSnapshot> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var subscription = new Subscription(this, metrics.Distinct().ToArray(),
            TimeSpan.FromMilliseconds(Math.Clamp(interval.TotalMilliseconds, 250, 60000)), callback);
        _subscriptions.Add(subscription);
        ReconfigureTimer();
        SampleDue(force: subscription);
        return subscription;
    }

    private void OnTick(object? sender, EventArgs e) => SampleDue();

    private void SampleDue(Subscription? force = null)
    {
        if (_disposed || _subscriptions.Count == 0) return;
        var now = DateTime.UtcNow;
        var due = _subscriptions.Where(item => ReferenceEquals(item, force) || item.NextDueUtc <= now).ToArray();
        if (due.Length == 0) return;
        var includeGpu = _subscriptions.Any(item => item.Metrics.Contains(MetricKind.SystemGpu));
        var snapshot = _sampler.Sample(includeGpu);
        foreach (var item in due)
        {
            item.NextDueUtc = now + item.Interval;
            item.Callback(snapshot);
        }
    }

    private void Remove(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        ReconfigureTimer();
        if (_subscriptions.All(item => !item.Metrics.Contains(MetricKind.SystemGpu)))
            _sampler.ReleaseGpu();
    }

    private void ReconfigureTimer()
    {
        _timer.Stop();
        if (_subscriptions.Count == 0) return;
        _timer.Interval = TimeSpan.FromMilliseconds(_subscriptions.Min(item => item.Interval.TotalMilliseconds));
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _subscriptions.Clear();
        _sampler.ReleaseGpu();
    }

    private sealed class Subscription(
        SystemMetricsMonitorService owner,
        MetricKind[] metrics,
        TimeSpan interval,
        Action<SystemMetricsSnapshot> callback) : IDisposable
    {
        public MetricKind[] Metrics { get; } = metrics;
        public TimeSpan Interval { get; } = interval;
        public Action<SystemMetricsSnapshot> Callback { get; } = callback;
        public DateTime NextDueUtc { get; set; }
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Remove(this);
        }
    }
}
