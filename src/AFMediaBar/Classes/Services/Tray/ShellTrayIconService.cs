using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理 AF Media Bar 的 Shell 通知区域图标及 Explorer 重启恢复。
/// Manages the AF Media Bar Shell notification icon and Explorer restart recovery.
/// </summary>
public sealed class ShellTrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_APP + 17;
    private readonly HwndSource _messageWindow;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private string _tooltipText = "AF Media Bar";
    private bool _isAdded;
    private bool _disposed;

    /// <summary>
    /// 创建隐藏消息窗口、注册 Explorer 重建消息并立即安装通知区域图标。
    /// Creates the hidden message window, registers for Explorer recreation, and installs the notification icon immediately.
    /// </summary>
    public ShellTrayIconService()
    {
        _messageWindow = new HwndSource(new HwndSourceParameters("AFMediaBar.ShellTrayWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_NOACTIVATE
        });
        _messageWindow.AddHook(WindowHook);
        _taskbarCreatedMessage = unchecked((uint)NativeMethods.RegisterWindowMessage("TaskbarCreated"));
        LoadApplicationIcon();
        AddIcon();
    }

    public event EventHandler? LeftClicked;
    public event EventHandler? ContextMenuRequested;
    public event EventHandler? TooltipOpening;
    public event EventHandler? ShellRestarted;

    /// <summary>
    /// 用户点击了气泡通知（不是超时或关闭）。调用方据此把用户带到相关界面。
    /// The user clicked the balloon notification, rather than letting it time out or dismissing it. Callers use this
    /// to take the user to the relevant surface.
    /// </summary>
    public event EventHandler? NotificationClicked;

    /// <summary>
    /// 弹出一次 Shell 气泡通知（Windows 10/11 上以系统通知的形式呈现）。
    ///
    /// 只做一次 Shell 调用，不排队也不重试：通知是"顺便告知"，失败不应该影响调用它的业务流程。
    /// 标题与正文按 Shell 的上限截断（标题 63、正文 255），超长会让整个调用被拒绝。
    /// Shows one Shell balloon notification, presented as a system notification on Windows 10/11.
    ///
    /// It is a single Shell call with no queueing and no retry: a notification is an aside, and its failure must not
    /// disturb the flow that asked for it. Title and body are truncated to the Shell's limits (63 and 255), because
    /// over-long text makes the whole call fail.
    /// </summary>
    /// <param name="title">通知标题。/ Notification title.</param>
    /// <param name="message">通知正文。/ Notification body.</param>
    /// <returns>Shell 是否接受了这次通知。/ Whether the Shell accepted the notification.</returns>
    public bool TryShowNotification(string title, string message)
    {
        if (!_isAdded)
        {
            return false;
        }

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = NativeMethods.NIIF_INFO;

        // NOTIFYICON_VERSION_4 之后 Shell 自己决定停留时长，这里的取值不会被使用，但保持有意义的默认值。
        // Since NOTIFYICON_VERSION_4 the Shell decides how long the balloon stays, so this value is unused while
        // still carrying a meaningful default.
        data.uTimeoutOrVersion = 10000;
        return NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private static string Truncate(string? text, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    /// <summary>
    /// 查询通知图标的物理屏幕边界，图标尚未安装或 Shell 查询失败时返回 <see langword="false"/>。
    /// Queries the notification icon bounds in physical screen coordinates; returns <see langword="false"/> when unavailable.
    /// </summary>
    public bool TryGetBounds(out TrayIconBounds bounds)
    {
        bounds = default;
        if (!_isAdded)
        {
            return false;
        }

        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _messageWindow.Handle,
            uID = IconId
        };
        if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
        {
            return false;
        }

        bounds = new TrayIconBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    /// <summary>
    /// 规范化并截断提示文本，然后在图标存在时同步更新 Shell 状态。
    /// Normalizes and truncates tooltip text, then updates Shell state when the icon is installed.
    /// </summary>
    public void UpdateTooltip(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? "AF Media Bar"
            : string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        _tooltipText = normalized.Length <= 127 ? normalized : normalized[..127];
        if (!_isAdded)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private IntPtr WindowHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (unchecked((uint)message) == _taskbarCreatedMessage)
        {
            _isAdded = false;
            AddIcon();
            ShellRestarted?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var notification = unchecked((int)(lParam.ToInt64() & 0xFFFF));
        if (notification is NativeMethods.NIN_SELECT or NativeMethods.NIN_KEYSELECT)
        {
            LeftClicked?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (notification == NativeMethods.WM_CONTEXTMENU)
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (notification == NativeMethods.NIN_POPUPOPEN)
        {
            TooltipOpening?.Invoke(this, EventArgs.Empty);
        }
        else if (notification == NativeMethods.NIN_BALLOONUSERCLICK)
        {
            NotificationClicked?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void LoadApplicationIcon()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        _ = NativeMethods.ExtractIconEx(executable, 0, out var large, out var small, 1);
        _icon = small != IntPtr.Zero ? small : large;
        if (_icon == small && large != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(large);
        }
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON |
            NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        _isAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_isAdded)
        {
            Debug.WriteLine($"[ShellTrayIconService] Add failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _messageWindow.Handle,
        uID = IconId,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltipText,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    /// <summary>
    /// 幂等移除通知图标、消息钩子和本服务拥有的原生图标句柄。
    /// Idempotently removes the notification icon, message hook, and native icon handle owned by this service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isAdded)
        {
            var data = CreateData();
            NativeMethods.ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _isAdded = false;
        }

        _messageWindow.RemoveHook(WindowHook);
        _messageWindow.Dispose();
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
