using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 封装窗口背景材质、DWM 属性、AccentPolicy 和 Popup Z 序的原生适配。
/// Encapsulates native adaptation for window backdrops, DWM attributes, AccentPolicy, and Popup Z-order.
/// </summary>
public sealed class NativeWindowBackdropAdapter
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmSystemBackdropType = 38;
    private const int DwmMicaEffect = 1029;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMica = 2;
    private const int DwmBackdropAcrylic = 3;
    private const int DwmBackdropMicaAlt = 4;
    private const int DwmCornerRound = 2;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// 创建原生窗口背景适配器。
    /// Creates the native window backdrop adapter.
    /// </summary>
    public NativeWindowBackdropAdapter()
    {
    }

    /// <summary>
    /// 清除句柄上的所有系统背景材质。
    /// Clears all system backdrop materials from the window handle.
    /// </summary>
    public void ResetBackdrop(nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        SetDwmAttribute(handle, DwmSystemBackdropType, DwmBackdropNone);
        SetDwmAttribute(handle, DwmMicaEffect, 0);
        ApplyAccentPolicy(handle, AccentState.Disabled, 0);
    }

    /// <summary>
    /// 按 Windows 版本应用 Mica、Mica Alt、Acrylic 或兼容的旧版材质。
    /// Applies Mica, Mica Alt, Acrylic, or a compatible legacy material according to the Windows version.
    /// </summary>
    /// <param name="handle">窗口句柄。/ Window handle.</param>
    /// <param name="mode">已经过 <see cref="WindowBackdropPolicy.Resolve"/> 回退的有效材质。/ Effective backdrop already run through <see cref="WindowBackdropPolicy.Resolve"/>.</param>
    /// <param name="tint">Accent 模糊路径的 ARGB 底色（来自材质浓度）。/ ARGB tint for the Accent blur path, taken from the material concentration.</param>
    public void ApplyBackdrop(nint handle, ApplicationBackdropMode mode, int tint)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        ResetBackdrop(handle);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            SetFrame(handle, extended: true);
            SetDwmAttribute(handle, DwmSystemBackdropType, mode switch
            {
                ApplicationBackdropMode.Mica => DwmBackdropMica,
                ApplicationBackdropMode.MicaAlt => DwmBackdropMicaAlt,
                _ => DwmBackdropAcrylic
            });
            return;
        }

        // 22000–22620 上只有旧式云母属性，没有云母 Alt；此处按云母处理，回退链已在上层策略里走过一遍。
        // Only the legacy Mica attribute exists on 22000-22620 and Mica Alt does not; it is treated as Mica here, the fallback
        // chain having already run in the policy above.
        if (mode is ApplicationBackdropMode.Mica or ApplicationBackdropMode.MicaAlt)
        {
            SetFrame(handle, extended: true);
            SetDwmAttribute(handle, DwmMicaEffect, 1);
            return;
        }

        SetFrame(handle, extended: false);
        ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    /// <summary>
    /// 为不获取焦点的短暂窗口应用材质。
    ///
    /// Windows 11 的三种系统材质（云母、亚克力、云母 Alt）由 DWM 采样桌面壁纸绘制，而 DWM 只为**前台**窗口绘制它们：
    /// 实测（26200）同一个非激活窗口上三种材质都是单色填充——窗口内亮度标准差 0.0，把同一个窗口激活后同一位置变成 262 种颜色、
    /// 标准差 14.4，并跟随背后壁纸的明暗。请求云母也一样退化成单色，因此"非激活窗口不要用系统材质"不是亚克力的特例，
    /// 而是所有系统材质的共同前提。
    ///
    /// 所以永不激活的窗口统一走 Accent 模糊路径：这条路径由窗口自己绘制，实测在非激活状态下依然在模糊背后的内容
    /// （窗口内亮度标准差 2.1–7.4，随浓度变化，而背后壁纸是 42.5），浓度由材质浓度设置给出。
    /// Applies a backdrop to a non-activating transient window.
    ///
    /// Windows 11's three system materials (Mica, Acrylic, Mica Alt) are painted by DWM from the desktop wallpaper, and DWM only
    /// paints them for the **foreground** window: on one and the same non-activated window all three measured as a flat fill — a
    /// luminance deviation of 0.0 inside the window, while activating that same window turned the same spot into 262 colours with a
    /// deviation of 14.4 that tracked the wallpaper behind it. Requesting Mica degrades just as much, so "no system material on a
    /// non-activating window" is a property of every system material rather than an Acrylic special case.
    ///
    /// Windows that never activate therefore all use the Accent blur path: that path is painted by the window itself and measured
    /// to keep blurring the content behind it while non-activated (a luminance deviation of 2.1-7.4 inside the window depending on
    /// concentration, against 42.5 for the wallpaper behind it), with its concentration coming from the material-concentration
    /// setting.
    /// </summary>
    /// <param name="handle">窗口句柄。/ Window handle.</param>
    /// <param name="mode">请求的有效材质；<see cref="ApplicationBackdropMode.FluentSolid"/> 由调用方处理。/ Requested effective backdrop; <see cref="ApplicationBackdropMode.FluentSolid"/> is handled by the caller.</param>
    /// <param name="tint">Accent 模糊路径的 ARGB 底色（来自材质浓度）。/ ARGB tint for the Accent blur path, taken from the material concentration.</param>
    public void ApplyNonActivatingBackdrop(nint handle, ApplicationBackdropMode mode, int tint)
    {
        if (handle == nint.Zero || mode == ApplicationBackdropMode.FluentSolid)
        {
            return;
        }

        ResetBackdrop(handle);
        SetFrame(handle, extended: false);
        ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    /// <summary>
    /// 设置 DWM 客户区扩展边距。
    /// Sets the DWM client-area frame extension margins.
    /// </summary>
    public void SetFrame(nint handle, bool extended)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        var margins = extended
            ? new DwmMargins(-1, -1, -1, -1)
            : new DwmMargins(0, 0, 0, 0);
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
    }

    /// <summary>
    /// 设置窗口标题栏和边框颜色是否透明。
    /// Sets whether the window caption and border colors are transparent.
    /// </summary>
    public void SetNonClientColors(nint handle, bool transparent)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        var color = transparent ? DwmColorNone : DwmColorDefault;
        SetDwmAttribute(handle, DwmCaptionColor, color);
        SetDwmAttribute(handle, DwmBorderColor, color);
    }

    /// <summary>
    /// 应用深色模式和圆角等通用 DWM 属性。
    /// Applies common DWM attributes such as dark mode and rounded corners.
    /// </summary>
    public void SetThemeAttributes(nint handle, bool dark)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        SetDwmAttribute(handle, DwmUseImmersiveDarkMode, dark ? 1 : 0);
        SetDwmAttribute(handle, DwmWindowCornerPreference, DwmCornerRound);
    }

    /// <summary>
    /// 将 Popup HWND 提升到菜单所需的非激活顶层 Z 序。
    /// Promotes a Popup HWND to the required non-activating top-level Z-order.
    /// </summary>
    public void PromotePopup(nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            handle,
            -1,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private static void SetDwmAttribute(nint handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    private static unsafe void ApplyAccentPolicy(nint handle, AccentState state, int gradientColor)
    {
        var policy = new AccentPolicy
        {
            State = state,
            GradientColor = gradientColor
        };
        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.AccentPolicy,
            Data = (nint)(&policy),
            SizeOfData = sizeof(AccentPolicy)
        };
        _ = SetWindowCompositionAttribute(handle, ref data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins
    {
        public DwmMargins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint handle, ref DwmMargins margins);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(nint handle, ref WindowCompositionAttributeData data);
}
