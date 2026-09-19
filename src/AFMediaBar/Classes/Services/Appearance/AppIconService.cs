using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using DrawingIcon = System.Drawing.Icon;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 按当前主题提供程序自身的图标：窗口与任务栏用 <see cref="ImageSource"/>，通知区域用 HICON 句柄。
///
/// 主题由 <see cref="ApplicationThemeManager"/> 发布（浅色 / 深色 / 高对比度），本服务据此挑一套图形，并在主题变化时
/// 通知窗口与托盘换图。窗口图标有两个来源都指向这里：<c>WindowAppearanceService</c> 在注册与主题变化时把它写到
/// 每个窗口的 <c>Icon</c> 上（任务栏按钮与 Alt+Tab 用的就是它），设置窗口的标题栏图标直接绑定本服务的
/// <c>TitleBarIconSource</c>。
/// Supplies the application's own icon for the active theme: an <see cref="ImageSource"/> for windows and the taskbar, and an
/// HICON handle for the notification area. The theme is published by <see cref="ApplicationThemeManager"/> (light, dark, high
/// contrast), this service picks an artwork for it, and both consumers are told when it changes: `WindowAppearanceService` writes
/// it to every window's `Icon` (which is what the taskbar button and Alt+Tab use) when a window is registered and whenever the
/// theme changes, while the settings window's title bar binds straight to `TitleBarIconSource`.
/// </summary>
public sealed class AppIconService : IDisposable
{
    private readonly Dictionary<string, ImageSource> _imageCache = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>创建图标服务并订阅主题变化。/ Creates the icon service and subscribes to theme changes.</summary>
    public AppIconService() => ApplicationThemeManager.Changed += OnApplicationThemeChanged;

    /// <summary>主题变化、图标应当换一套时触发。/ Raised when the theme changed and the artwork should be swapped.</summary>
    public event Action? IconChanged;

    /// <summary>
    /// 当前主题该用的窗口图标。
    ///
    /// 取图标里最大的一帧并冻结：窗口图标由系统按显示尺寸缩放，给最大帧不会糊，而冻结之后同一份对象可以安全地在多个
    /// 窗口与多个线程之间共享，不必每帧重新解码。
    /// The window icon for the current theme. The largest frame is taken and frozen: the system scales a window icon to the size it
    /// needs, so handing over the largest frame keeps it sharp, and a frozen object can be shared between windows and threads
    /// without decoding it again on every frame.
    /// </summary>
    /// <returns>图标；资源不可用时为 null（调用方保留原图标）。/ The icon, or null when the resource is unavailable.</returns>
    public ImageSource? ResolveImageSource()
    {
        var uri = AppIconPolicy.ResolveArtworkUri(ApplicationThemeManager.GetAppTheme(), SystemColors.WindowColor);
        if (_imageCache.TryGetValue(uri, out var cached))
        {
            return cached;
        }

        try
        {
            // BitmapCacheOption.OnLoad：解码在构造时完成，之后不再持有任何文件或流句柄。
            // BitmapCacheOption.OnLoad decodes during construction, so no file or stream handle stays open afterwards.
            using var stream = Application.GetResourceStream(new Uri(uri, UriKind.Absolute)).Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).First();
            frame.Freeze();
            _imageCache[uri] = frame;
            return frame;
        }
        catch (Exception exception)
        {
            // 图标缺失不该让窗口打不开：调用方保留原来的图标并继续。
            // A missing icon must not stop a window from opening: the caller keeps the icon it already had and carries on.
            Debug.WriteLine($"[AppIconService] Could not load {uri}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 当前主题该用的托盘图标。返回值的 <c>Handle</c> 可以交给 Shell，用完后由调用方释放——句柄的所有权跟着返回值走，
    /// 因此托盘服务释放它自己的那一份，不需要知道图标是从哪来的。
    /// The tray icon for the current theme. Its `Handle` may be handed to the Shell and the caller releases it when done: ownership
    /// travels with the return value, so the tray service frees what it holds without knowing where the icon came from.
    /// </summary>
    /// <returns>图标句柄的所有权包；资源不可用时为 null。/ An ownership wrapper for the icon handle, or null when unavailable.</returns>
    public TrayIcon? ResolveTrayIcon()
    {
        var uri = AppIconPolicy.ResolveArtworkUri(ApplicationThemeManager.GetAppTheme(), SystemColors.WindowColor);
        try
        {
            using var stream = Application.GetResourceStream(new Uri(uri, UriKind.Absolute)).Stream;
            var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            bytes.Position = 0;
            return new TrayIcon(bytes);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AppIconService] Could not load {uri} for the tray: {exception.Message}");
            return null;
        }
    }

    /// <summary>把一个窗口的图标换成当前主题对应的那一套。/ Swaps one window's icon to the one for the current theme.</summary>
    /// <param name="window">目标窗口。/ Target window.</param>
    public void ApplyTo(Window window)
    {
        if (_disposed || ResolveImageSource() is not { } source)
        {
            return;
        }

        if (!ReferenceEquals(window.Icon, source))
        {
            window.Icon = source;
        }
    }

    private void OnApplicationThemeChanged(ApplicationTheme theme, Color accent)
    {
        if (_disposed)
        {
            return;
        }

        IconChanged?.Invoke();
    }

    /// <summary>退订主题变化。/ Unsubscribes from theme changes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
    }

    /// <summary>
    /// 托盘图标句柄的所有权包。
    ///
    /// <see cref="DrawingIcon"/> 在部分实现里会延迟读取来源流，因此字节流必须与图标同时存活；把两者放在一起，"谁释放"
    /// 就只有一个答案：拿到这个包的人。
    /// An ownership wrapper for a tray icon handle. <see cref="DrawingIcon"/> reads its source stream lazily in some
    /// implementations, so the byte stream has to stay alive next to the icon; keeping both together leaves exactly one answer to
    /// "who releases this": whoever received the wrapper.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly MemoryStream _stream;

        /// <summary>从图标字节流创建所有权包。/ Creates the wrapper from an icon byte stream.</summary>
        /// <param name="stream">图标字节流，所有权转移给本对象。/ Icon byte stream; ownership moves to this object.</param>
        public TrayIcon(MemoryStream stream)
        {
            _stream = stream;
            Icon = new DrawingIcon(stream);
        }

        /// <summary>底层 GDI+ 图标对象。/ The underlying GDI+ icon object.</summary>
        public DrawingIcon Icon { get; }

        /// <summary>可交给 Shell 的 HICON 句柄。/ The HICON handle that may be handed to the Shell.</summary>
        public IntPtr Handle => Icon.Handle;

        /// <summary>释放图标句柄与它的字节流。/ Releases the icon handle and its byte stream.</summary>
        public void Dispose()
        {
            Icon.Dispose();
            _stream.Dispose();
        }
    }
}
