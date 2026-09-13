using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>解析固定显示器、回退顺序和旧索引迁移。 / Resolves fixed displays, fallback order, and legacy-index migration.</summary>
public static class DisplayTargetPolicy
{
    /// <summary>按通知模式解析前台或固定目标，并应用固定目标回退链。 / Resolves a foreground or fixed notification target and applies the fixed-target fallback chain.</summary>
    public static DisplayMonitorInfo? ResolveNotification(
        IReadOnlyList<DisplayMonitorInfo> monitors,
        NotificationTargetMode mode,
        string? fixedDeviceId,
        string? foregroundDeviceId)
    {
        if (mode == NotificationTargetMode.ForegroundWindow && !string.IsNullOrWhiteSpace(foregroundDeviceId))
        {
            var foreground = monitors.FirstOrDefault(monitor => string.Equals(
                monitor.DeviceId,
                foregroundDeviceId,
                StringComparison.OrdinalIgnoreCase));
            if (foreground is not null)
                return foreground;
        }

        return ResolveFixed(monitors, fixedDeviceId);
    }

    /// <summary>按设备标识解析显示器，缺失时回退到主屏或首屏。 / Resolves a device identifier, falling back to the primary or first display.</summary>
    public static DisplayMonitorInfo? ResolveFixed(
        IReadOnlyList<DisplayMonitorInfo> monitors,
        string? deviceId)
    {
        if (monitors.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var matched = monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
                return matched;
        }

        return monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];
    }

    /// <summary>把旧的排序索引最佳努力映射成当前设备标识。 / Best-effort maps a legacy sorted index to a current device identifier.</summary>
    public static string? ResolveLegacyDeviceId(IReadOnlyList<DisplayMonitorInfo> monitors, int index)
    {
        if (monitors.Count == 0)
            return null;

        return monitors[Math.Clamp(index, 0, monitors.Count - 1)].DeviceId;
    }
}
