using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 生成托盘音频状态提示文本，不执行音频读取或 UI 更新。
/// Builds tray audio status text without performing audio reads or UI updates.
/// </summary>
public static class AudioTooltipPolicy
{
    /// <summary>
    /// 根据滚轮模式、当前媒体应用和默认输出设备生成提示。
    /// Builds tooltip text from wheel mode, current media application, and default output device.
    /// </summary>
    public static string Build(
        TrayWheelBehavior behavior,
        ApplicationVolumeSnapshot? application,
        AudioDeviceOption? device)
    {
        return behavior switch
        {
            TrayWheelBehavior.AdjustVolume => BuildMediaVolume(application?.DisplayName, application?.VolumePercent),
            TrayWheelBehavior.SwitchOutputDevice => BuildOutputDevice(device),
            _ => "托盘滚轮已禁用"
        };
    }

    /// <summary>
    /// 生成输出设备提示；托盘与任务栏悬停层按钮共用同一措辞。
    /// Builds the output-device tooltip; the tray and the taskbar hover-layer button share this wording.
    /// </summary>
    public static string BuildOutputDevice(AudioDeviceOption? device) =>
        device is null ? "输出设备：不可用" : $"输出设备：{device.DisplayName}";

    /// <summary>
    /// 生成当前媒体音量提示；无媒体应用或音量不可读时给出明确回退。
    /// Builds the current-media-volume tooltip, with an explicit fallback when no media application or volume is available.
    /// </summary>
    public static string BuildMediaVolume(string? displayName, int? volumePercent) =>
        string.IsNullOrWhiteSpace(displayName) || volumePercent is null
            ? "当前媒体音量：不可用"
            : $"{displayName}：{volumePercent}%";
}
