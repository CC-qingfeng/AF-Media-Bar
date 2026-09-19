using System.Text.RegularExpressions;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 程序自身图标的主题映射，以及"被引用的图标素材必须真的存在"这条守卫。
/// The theme mapping of the application's own icon, plus the guard that every referenced icon asset really exists.
/// </summary>
[TestClass]
public sealed class AppIconTests
{
    /// <summary>
    /// 深色主题用白色图形：深色标题栏与深色任务栏上，深色图形是看不见的。
    /// The dark theme uses the white artwork: dark artwork is invisible on a dark title bar and a dark taskbar.
    /// </summary>
    [TestMethod]
    public void DarkThemeUsesTheWhiteArtwork()
    {
        Assert.AreEqual(
            AppIconPolicy.LightArtworkUri,
            AppIconPolicy.ResolveArtworkUri(ApplicationTheme.Dark, Colors.White));
    }

    /// <summary>浅色主题用深色图形。/ The light theme uses the dark artwork.</summary>
    [TestMethod]
    public void LightThemeUsesTheDarkArtwork()
    {
        Assert.AreEqual(
            AppIconPolicy.DarkArtworkUri,
            AppIconPolicy.ResolveArtworkUri(ApplicationTheme.Light, Colors.White));
    }

    /// <summary>
    /// 高对比度按系统窗口底色决定，而不是一律当成浅色：那套配色可能是黑底白字，也可能是白底黑字。
    /// High contrast is decided by the system window colour rather than being treated as light: that scheme may be white-on-black or
    /// black-on-white.
    /// </summary>
    [TestMethod]
    public void HighContrastFollowsTheSystemWindowColour()
    {
        Assert.AreEqual(
            AppIconPolicy.LightArtworkUri,
            AppIconPolicy.ResolveArtworkUri(ApplicationTheme.HighContrast, Colors.Black),
            "黑底高对比度必须用白色图形。");

        Assert.AreEqual(
            AppIconPolicy.DarkArtworkUri,
            AppIconPolicy.ResolveArtworkUri(ApplicationTheme.HighContrast, Colors.White),
            "白底高对比度必须用深色图形。");
    }

    /// <summary>深浅判定按人眼敏感度加权，中灰两侧各取一端。/ Darkness is decided by sensitivity-weighted luminance, one end either side of mid grey.</summary>
    [TestMethod]
    public void DarknessUsesWeightedLuminance()
    {
        Assert.IsTrue(AppIconPolicy.IsDark(Colors.Black));
        Assert.IsTrue(AppIconPolicy.IsDark(Color.FromRgb(0x20, 0x20, 0x20)));
        Assert.IsFalse(AppIconPolicy.IsDark(Colors.White));
        Assert.IsFalse(AppIconPolicy.IsDark(Color.FromRgb(0xF3, 0xF3, 0xF3)));

        // 纯绿比纯蓝亮得多：换成简单平均就会把两者判成同一档。
        // Pure green is much brighter than pure blue; a plain average would put the two in the same bucket.
        Assert.IsFalse(AppIconPolicy.IsDark(Colors.Lime));
        Assert.IsTrue(AppIconPolicy.IsDark(Colors.Blue));
    }

    /// <summary>
    /// 托盘跟系统模式（任务栏）而不是本程序主题：Windows 允许"任务栏浅色 + 应用深色"这类组合，跟错一方就会出现深色图形
    /// 贴在深色任务栏上。窗口图标仍然跟本程序主题。
    /// The tray follows the system mode (taskbar) rather than this application's theme: Windows allows a light taskbar with dark
    /// applications, and following the wrong one leaves dark artwork on a dark taskbar. Window artwork still follows the app theme.
    /// </summary>
    [TestMethod]
    public void TrayFollowsTheSystemModeInsteadOfTheApplicationTheme()
    {
        Assert.AreEqual(
            AppIconPolicy.DarkArtworkUri,
            AppIconPolicy.ResolveTrayArtworkUri(WindowsThemeDetector.ThemeMode.Light, ApplicationTheme.Dark, Colors.White),
            "浅色任务栏必须用深色图形，即使应用主题是深色。");

        Assert.AreEqual(
            AppIconPolicy.LightArtworkUri,
            AppIconPolicy.ResolveTrayArtworkUri(WindowsThemeDetector.ThemeMode.Dark, ApplicationTheme.Light, Colors.Black),
            "深色任务栏必须用白色图形，即使应用主题是浅色。");
    }

    /// <summary>系统模式读不到时退回本程序主题。/ The application theme is the fallback when the system mode cannot be read.</summary>
    [TestMethod]
    public void TrayFallsBackToTheApplicationThemeWhenTheSystemModeIsUnknown()
    {
        Assert.AreEqual(
            AppIconPolicy.LightArtworkUri,
            AppIconPolicy.ResolveTrayArtworkUri(WindowsThemeDetector.ThemeMode.Unknown, ApplicationTheme.Dark, Colors.White));
    }

    /// <summary>
    /// 每一处被引用的图标素材都必须真的存在。
    ///
    /// 这条守卫来自一次真实事故：四张旧图标被删除、两张新图标放进来，而 `.csproj`、`SettingsWindow.xaml` 与安装脚本仍然
    /// 指向旧文件。XAML 上的表现是图标画不出来，安装脚本上的表现是 CI 直接编译失败，两者都不会出现在"生成成功"里。
    /// Every referenced icon asset has to exist. This guard comes from a real incident: four old icons were deleted and two new ones
    /// added while the `.csproj`, `SettingsWindow.xaml` and the installer script still pointed at the old files. In XAML the symptom
    /// is an icon that never draws, in the installer script it is a CI build that fails, and neither appears in a successful build.
    /// </summary>
    [TestMethod]
    public void EveryReferencedIconAssetExists()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "src", "AFMediaBar");
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var xaml in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories).Where(IsSourceFile))
        {
            var text = File.ReadAllText(xaml);
            foreach (Match match in Regex.Matches(text, @"pack://application:,,,/(Assets/[^""'\s]+)"))
            {
                checkedCount++;
                var target = Path.Combine(appRoot, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(target))
                {
                    missing.Add($"{Path.GetFileName(xaml)} → {match.Groups[1].Value}");
                }
            }
        }

        var projectFile = Path.Combine(appRoot, "AFMediaBar.csproj");
        foreach (Match match in Regex.Matches(File.ReadAllText(projectFile), @"(?:ApplicationIcon|Include|Remove)=""(Assets\\[^""]+)"""))
        {
            var relative = match.Groups[1].Value;

            // 通配项允许匹配到零个文件（赞助收款码就是"用户以后再放"的），因此只检查具体文件名。
            // A wildcard entry may legitimately match nothing (the sponsor payment codes are added later), so only concrete names
            // are checked.
            if (relative.Contains('*'))
            {
                continue;
            }

            checkedCount++;
            if (!File.Exists(Path.Combine(appRoot, relative)))
            {
                missing.Add($"AFMediaBar.csproj → {relative}");
            }
        }

        var installerScript = Path.Combine(root, "installer", "AFMediaBar.iss");
        foreach (Match match in Regex.Matches(File.ReadAllText(installerScript), @"\.\.\\src\\AFMediaBar\\([^""\r\n]+)"))
        {
            checkedCount++;
            var relative = match.Groups[1].Value;
            if (!File.Exists(Path.Combine(appRoot, relative)))
            {
                missing.Add($"AFMediaBar.iss → {relative}");
            }
        }

        Assert.IsTrue(checkedCount >= 4, $"只检查到 {checkedCount} 处素材引用，扫描逻辑可能已经失效。");
        Assert.AreEqual(
            0,
            missing.Count,
            "以下位置引用了不存在的素材：\n  " + string.Join("\n  ", missing));
    }

    /// <summary>排除 obj 与 bin：生成目录里会出现从 XAML 复制出来的中间文件。/ Excludes obj and bin, whose generated copies of XAML would otherwise be scanned too.</summary>
    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>从测试输出目录向上找到含解决方案与主项目的仓库根。/ Walks up from the test output directory to the repository root holding the solution and the app project.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AFMediaBar", "AFMediaBar.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
