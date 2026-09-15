using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>性能指标轮换和文本格式化纯策略。 / Pure policy for compact metric cycling and formatting.</summary>
public static class MetricPresentationPolicy
{
    public static int Advance(int currentIndex, int publishedSampleCount, int metricCount) =>
        metricCount <= 1 || publishedSampleCount <= 0 || publishedSampleCount % 3 != 0
            ? Math.Clamp(currentIndex, 0, Math.Max(0, metricCount - 1))
            : (Math.Clamp(currentIndex, 0, metricCount - 1) + 1) % metricCount;

    public static string Format(MetricKind metric, SystemMetricsSnapshot snapshot) => metric switch
    {
        MetricKind.SystemMemory => $"MEM {snapshot.SystemMemoryPercent}%",
        MetricKind.SystemCpu => snapshot.SystemCpuPercent is int cpu ? $"CPU {cpu}%" : "CPU —",
        MetricKind.SystemGpu => snapshot.SystemGpuPercent is int gpu ? $"GPU {gpu}%" : "GPU —",
        MetricKind.ProcessMemory => $"APP {snapshot.ProcessMemoryMegabytes} MB",
        _ => "—"
    };
}
