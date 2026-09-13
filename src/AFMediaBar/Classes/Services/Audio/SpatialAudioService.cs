using System.Diagnostics;
using AFMediaBar.Classes.Models;
using Windows.Media.Audio;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 读取当前设备的空间音效状态，并通过系统设置提供受支持的配置入口。
/// Reads spatial-audio state and delegates configuration to the supported Windows settings surface.
/// </summary>
public sealed class SpatialAudioService
{
    private static readonly IReadOnlyDictionary<string, string> KnownFormats =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SpatialAudioFormatSubtype.WindowsSonic] = "Windows Sonic",
            [SpatialAudioFormatSubtype.DolbyAtmosForHeadphones] = "Dolby Atmos for Headphones",
            [SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater] = "Dolby Atmos for Home Theater",
            [SpatialAudioFormatSubtype.DolbyAtmosForSpeakers] = "Dolby Atmos for Speakers",
            [SpatialAudioFormatSubtype.DTSHeadphoneX] = "DTS Headphone:X",
            [SpatialAudioFormatSubtype.DTSXUltra] = "DTS:X Ultra"
        };

    /// <summary>
    /// 查询指定输出设备的空间音效状态；平台不支持或查询失败时返回不可用快照。
    /// Queries spatial-audio state for an output device and returns an unavailable snapshot when unsupported or failed.
    /// </summary>
    public SpatialAudioSnapshot GetState(string deviceId)
    {
        try
        {
            var configuration = SpatialAudioDeviceConfiguration.GetForDeviceId(deviceId);
            if (!configuration.IsSpatialAudioSupported)
            {
                return new SpatialAudioSnapshot(false, "此设备不支持", null);
            }

            var active = configuration.ActiveSpatialAudioFormat;
            var selected = string.IsNullOrWhiteSpace(active)
                ? configuration.DefaultSpatialAudioFormat
                : active;
            var name = string.IsNullOrWhiteSpace(selected)
                ? "关闭"
                : TryGetKnownFormatName(selected, out var known) ? known : "已启用";
            return new SpatialAudioSnapshot(true, name, selected);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[SpatialAudioService] Read failed: {exception.Message}");
            return new SpatialAudioSnapshot(false, "状态不可用", null);
        }
    }

    /// <summary>
    /// 打开 Windows 声音设置，由系统界面负责后续空间音效配置。
    /// Opens Windows sound settings and delegates further spatial-audio configuration to the system UI.
    /// </summary>
    public void OpenSystemSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:sound-devices") { UseShellExecute = true });
    }

    private static bool TryGetKnownFormatName(string subtype, out string name)
    {
        if (KnownFormats.TryGetValue(subtype, out name!))
        {
            return true;
        }

        if (!Guid.TryParse(subtype, out var candidate))
        {
            name = string.Empty;
            return false;
        }

        foreach (var format in KnownFormats)
        {
            if (Guid.TryParse(format.Key, out var known) && candidate == known)
            {
                name = format.Value;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }
}
