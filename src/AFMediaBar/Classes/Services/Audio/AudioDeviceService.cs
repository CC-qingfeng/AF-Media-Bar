using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 枚举输出设备，并切换默认 Console/Multimedia 端点。
///
/// 设备名来自系统（Windows 报告的端点名），不做翻译；只有创建 COM 组件失败时的原因文本按当前界面语言取值。
/// Enumerates render devices and switches the default Console/Multimedia endpoint.
///
/// Device names come from the system (the endpoint names Windows reports) and are not translated; only the reason text for a
/// failed COM component creation follows the active interface language.
/// </summary>
public sealed class AudioDeviceService
{
    private static readonly Guid PolicyConfigClientClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    /// <summary>
    /// 异步枚举活动渲染设备，并返回可安全绑定到 UI 的不可变选项列表。
    /// Asynchronously enumerates active render devices and returns an immutable option list safe for UI binding.
    /// </summary>
    public async Task<IReadOnlyList<AudioDeviceOption>> GetRenderDevicesAsync()
    {
        var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return devices
            .Where(device => device.IsEnabled)
            .Select(device => new AudioDeviceOption(
                device.Id,
                GetPolicyDeviceId(device.Id),
                string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
            // 默认状态只决定选中项，不参与排序，避免切换后设备位置跳动。
            // Default status controls selection only, keeping wheel indexes stable after a switch.
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 判断指定设备是否已经是当前默认音频输出。
    /// Determines whether the specified device is already the current default audio output.
    /// </summary>
    public bool IsDefaultRenderDevice(string deviceId) =>
        string.Equals(
            MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default),
            deviceId,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 通过 Windows 策略配置接口切换默认渲染设备；空标识不会触发原生调用。
    /// Changes the default render device through the Windows policy configuration interface; empty identifiers do not invoke native code.
    /// </summary>
    public void SetDefaultRenderDevice(string policyDeviceId)
    {
        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(PolicyConfigClientClassId, throwOnError: true)!;
            client = Activator.CreateInstance(type) ??
                throw new InvalidOperationException(Translations.Get("Audio.Error.CreatePolicyConfig"));
            var policy = (IPolicyConfig)client;
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(policyDeviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(policyDeviceId, ERole.Multimedia));
        }
        finally
        {
            if (client is not null && Marshal.IsComObject(client))
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    private static string GetPolicyDeviceId(string deviceInformationId)
    {
        const string marker = "MMDEVAPI#";
        var start = deviceInformationId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return deviceInformationId;
        }

        start += marker.Length;
        var end = deviceInformationId.IndexOf("#{", start, StringComparison.OrdinalIgnoreCase);
        return end > start ? deviceInformationId[start..end] : deviceInformationId[start..];
    }

    private enum ERole { Console, Multimedia, Communications }

    // vtable 顺序来自 Windows PolicyConfig ABI，未使用槽位仍须保留。
    // The vtable follows the Windows PolicyConfig ABI; unused slots must remain.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string id, out nint format);
        int GetDeviceFormat(string id, int defaultFormat, out nint format);
        int ResetDeviceFormat(string id);
        int SetDeviceFormat(string id, nint endpointFormat, nint mixFormat);
        int GetProcessingPeriod(string id, int defaultPeriod, out long period, out long minimumPeriod);
        int SetProcessingPeriod(string id, ref long period);
        int GetShareMode(string id, out nint mode);
        int SetShareMode(string id, nint mode);
        int GetPropertyValue(string id, int store, nint key, out nint value);
        int SetPropertyValue(string id, int store, nint key, nint value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
