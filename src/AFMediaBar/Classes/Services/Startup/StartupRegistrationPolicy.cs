namespace AFMediaBar.Classes.Services;

/// <summary>
/// 开机自动启动的纯策略：命令行拼装与"这条记录是否属于当前程序"的判定。
/// Pure policy for run-at-startup: command-line assembly and deciding whether an existing entry belongs to this application.
/// </summary>
public static class StartupRegistrationPolicy
{
    /// <summary>
    /// 拼装启动项命令行。路径一律加引号：程序目录可能含空格，不加引号时 Windows 会把第一个空格当作参数分隔符，
    /// 于是启动的是另一个路径，用户看到的只是"开机后没启动"。
    /// Builds the startup entry's command line. The path is always quoted: the installation directory can contain spaces, and
    /// without quotes Windows splits at the first space, launching a different path while the user only sees that nothing started.
    /// </summary>
    /// <param name="executablePath">可执行文件的完整路径。/ Full path of the executable.</param>
    public static string BuildCommandLine(string executablePath) => $"\"{executablePath.Trim().Trim('"')}\"";

    /// <summary>
    /// 判断启动项记录是否指向当前程序。比较时忽略外层引号与大小写，并允许记录里带参数。
    /// Decides whether a startup entry points at this application, ignoring surrounding quotes and case, and tolerating arguments.
    /// </summary>
    /// <param name="registeredCommand">注册表里的命令行；缺失时传 null。/ Command line read from the registry, or null when absent.</param>
    /// <param name="executablePath">当前可执行文件的完整路径。/ Full path of the current executable.</param>
    public static bool Matches(string? registeredCommand, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(registeredCommand) || string.IsNullOrWhiteSpace(executablePath))
            return false;

        var registered = ExtractExecutable(registeredCommand);
        return registered is not null &&
               string.Equals(registered, executablePath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractExecutable(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed[0] == '"')
        {
            var closing = trimmed.IndexOf('"', 1);
            return closing > 1 ? trimmed[1..closing] : null;
        }

        var separator = trimmed.IndexOf(' ');
        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
