namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 是否允许启动安装包，以及应当如何启动。
/// Whether the installer may be started, and how.
/// </summary>
/// <param name="CanInstall">当前是否可以安装。/ Whether installing is possible right now.</param>
/// <param name="UseRunAs">机器级安装且当前未提权时为 true：需要一次 UAC 提权。/ True for a machine-wide install from a non-elevated process, which needs one UAC prompt.</param>
/// <param name="BlockedReason">不能安装时的原因；可安装时为 null。/ Reason shown when installing is impossible, or null.</param>
public sealed record UpdateInstallDecision(bool CanInstall, bool UseRunAs, string? BlockedReason);

/// <summary>
/// 静默安装的参数与前置条件策略。
///
/// 参数串是与安装程序的公开契约（见 <c>installer/README.md</c>）：<c>/SILENT</c> 不显示向导但保留进度窗口，
/// 因此不用 <c>/VERYSILENT</c>——用户要求能看到安装进度。是否在装完后启动程序由 <c>AUTORELAUNCH</c> 决定：
/// 只有"立即重启并安装"传 1，"用户正常退出时顺带安装"传 0，否则用户刚关掉的窗口会自己回来。
/// Arguments and preconditions for the silent install.
///
/// The argument string is the public contract with the installer (see <c>installer/README.md</c>): <c>/SILENT</c>
/// hides the wizard but keeps the progress window, which is why <c>/VERYSILENT</c> is not used — the update is
/// supposed to be observable. Whether the application comes back is decided by <c>AUTORELAUNCH</c>: only
/// "restart and install now" passes 1, while "install alongside a normal quit" passes 0 so a window the user just
/// closed does not return on its own.
/// </summary>
public static class UpdateInstallPlanPolicy
{
    /// <summary>便携版不能自动安装：没有安装记录，也就没有可以原位替换的目录。/ A portable copy cannot be installed automatically, having no installation to replace in place.</summary>
    public const string PortableBlockedReason = "当前是便携版，无法自动安装";

    /// <summary>其它实例在运行时推迟安装，避免两个实例争抢同一份设置与托盘图标。/ Installing is deferred while other instances run, so two instances cannot fight over settings and the tray icon.</summary>
    public const string OtherInstancesBlockedReason = "程序还有其它实例在运行";

    /// <summary>
    /// 组装静默安装参数。
    /// Builds the silent install arguments.
    /// </summary>
    /// <param name="relaunchAfterInstall">安装完成后是否启动新版本。/ Whether to start the new version afterwards.</param>
    /// <param name="logPath">安装日志路径；会被引号包裹以容忍空格。/ Install log path, quoted so spaces are tolerated.</param>
    /// <returns>完整参数串。/ Complete argument string.</returns>
    public static string BuildArguments(bool relaunchAfterInstall, string logPath) =>
        "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS " +
        $"/AUTORELAUNCH={(relaunchAfterInstall ? "1" : "0")} " +
        $"/LOG=\"{logPath}\"";

    /// <summary>
    /// 决定现在能否安装。
    /// Decides whether installing is possible now.
    /// </summary>
    /// <param name="isInstalledCopy">当前进程是否来自安装程序安装的目录。/ Whether the running process lives in a directory created by the installer.</param>
    /// <param name="isMachineWide">该安装是否为所有用户（注册表项在 HKLM）。/ Whether the installation is machine-wide, which puts its registry entry under HKLM.</param>
    /// <param name="isElevated">当前进程是否已提权。/ Whether the current process is elevated.</param>
    /// <param name="otherInstanceCount">其它正在运行的程序实例数量。/ Number of other running instances of the application.</param>
    /// <returns>是否可以安装、是否需要提权，以及不能安装时的原因。/ Whether to install, whether to elevate, and the reason when blocked.</returns>
    public static UpdateInstallDecision Decide(
        bool isInstalledCopy,
        bool isMachineWide,
        bool isElevated,
        int otherInstanceCount)
    {
        if (!isInstalledCopy)
        {
            return new UpdateInstallDecision(false, false, PortableBlockedReason);
        }

        if (otherInstanceCount > 0)
        {
            return new UpdateInstallDecision(false, false, OtherInstancesBlockedReason);
        }

        // 机器级安装写的是 Program Files，未提权时只能请用户确认一次。
        // A machine-wide installation writes into Program Files, so a non-elevated process must ask once.
        return new UpdateInstallDecision(true, isMachineWide && !isElevated, null);
    }
}
