using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Audio;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将媒体来源标识解析为可激活的窗口、应用或可执行文件。
/// Resolves a media source identifier to an activatable window, app, or executable.
/// </summary>
public sealed class MediaSourceActivationService
{
    private readonly MediaSourceProcessResolver _processResolver;

    /// <summary>
    /// 创建来源激活器，并复用音频来源解析规则定位候选进程。
    /// Creates the source activator and reuses audio-source resolution rules to locate candidate processes.
    /// </summary>
    public MediaSourceActivationService(MediaSourceProcessResolver processResolver)
    {
        _processResolver = processResolver;
    }

    /// <summary>
    /// 尝试恢复并前置来源进程的主窗口；找不到安全窗口时保持无操作。
    /// Attempts to restore and foreground the source process main window, becoming a no-op when no safe window is found.
    /// </summary>
    public void Activate(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in _processResolver.ResolveProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                        {
                            var executablePath = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(executablePath))
                            {
                                executablePaths.Add(executablePath);
                            }

                            continue;
                        }

                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                        return;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                                                   System.ComponentModel.Win32Exception or
                                                   NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Process exited while activating: {ex.Message}");
                    }
                }
            }
        }

        try
        {
            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{sourceId}",
                    UseShellExecute = true
                });
                return;
            }

            var executablePath = executablePaths.FirstOrDefault(File.Exists);
            if (executablePath is not null)
            {
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSourceActivationService] Activate source failed: {ex}");
        }
    }

}
