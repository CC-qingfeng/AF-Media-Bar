using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>解析任务栏媒体栏要承载在哪些显示器上。/ Resolves which monitor taskbars should host the media bar.</summary>
public static class TaskbarTargetPolicy
{
    /// <summary>设置文件里表示“所有任务栏”的稳定标识。/ Stable settings identifier for “all taskbars”.</summary>
    public const string AllTaskbarsDeviceId = "{AFMediaBar.AllTaskbars}";

    /// <summary>判断设置值是否要求在所有任务栏上显示。/ Determines whether the setting requests every taskbar.</summary>
    public static bool IsAllTaskbars(string? deviceId) =>
        string.Equals(deviceId, AllTaskbarsDeviceId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 返回目标显示器标识；所有任务栏模式按主屏优先稳定排序，单屏模式沿用固定目标的断开回退。
    /// Returns target device identifiers; all-taskbars mode is stably ordered with the primary first, while single-target mode keeps
    /// the fixed monitor's disconnect fallback.
    /// </summary>
    public static IReadOnlyList<string> ResolveDeviceIds(
        IReadOnlyList<DisplayMonitorInfo> monitors,
        string? configuredDeviceId)
    {
        if (monitors.Count == 0)
            return [];

        if (IsAllTaskbars(configuredDeviceId))
        {
            return monitors
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenBy(monitor => monitor.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(monitor => monitor.DeviceId)
                .ToArray();
        }

        var fixedMonitor = DisplayTargetPolicy.ResolveFixed(monitors, configuredDeviceId);
        return fixedMonitor is null ? [] : [fixedMonitor.DeviceId];
    }
}
