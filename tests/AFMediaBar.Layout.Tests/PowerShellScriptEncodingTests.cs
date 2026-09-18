using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 脚本编码检查：含非 ASCII 字符的 PowerShell 脚本必须是 **UTF-8 with BOM**。
///
/// 为什么值得一条测试：Windows PowerShell 5.1 会把无 BOM 的 UTF-8 当 ANSI 读，中文注释与字符串于是在解析阶段就变成乱码——
/// 报错形式是"意外的标记"，与脚本逻辑毫无关系，而这类文件的读者（维护者、发布者）常常正是直接用 5.1 双击或 `.\x.ps1` 运行它。
/// 这个坑已经真实发生过一次（`tools/update-contributors.ps1` 首次提交即无法运行），而任何一次"用普通编辑器改一行再保存"
/// 都会把它带回来，因此这里把它钉死。
/// Script-encoding check: PowerShell scripts that contain non-ASCII characters have to be **UTF-8 with BOM**.
///
/// Why this earns a test: Windows PowerShell 5.1 reads UTF-8 without a BOM as ANSI, so the Chinese comments and strings turn into mojibake during
/// parsing — the error reads "unexpected token" and has nothing to do with the script's logic — while the people reading these files (maintainers,
/// the release side) are exactly the ones who run them with 5.1 by double-clicking or `.\x.ps1`. This has already happened once
/// (`tools/update-contributors.ps1` could not run as first committed), and any "open it in an editor, change one line, save" brings it back, so it
/// is pinned down here.
/// </summary>
[TestClass]
public sealed class PowerShellScriptEncodingTests
{
    [TestMethod]
    public void ScriptsWithNonAsciiTextCarryAUtf8Bom()
    {
        var problems = new List<string>();
        var checkedCount = 0;

        foreach (var file in EnumeratePowerShellScripts())
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length == 0)
            {
                continue;
            }

            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
            var hasNonAscii = text.Any(character => character > 127);
            if (!hasNonAscii)
            {
                // 纯 ASCII 脚本不受这个坑影响：它在任何代码页下都读得一样。
                // A pure-ASCII script is immune: it reads identically under every code page.
                continue;
            }

            checkedCount++;
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (!hasBom)
            {
                problems.Add($"{Path.GetFileName(file)} 含非 ASCII 文本但不是 UTF-8 with BOM，Windows PowerShell 5.1 会把中文读成乱码并报语法错误");
            }
        }

        // 自检：确认扫描真的覆盖到了脚本，否则这条规则只是一段永远通过的代码。
        // Self-check: the scan has to actually reach the scripts, otherwise this rule is code that can only ever pass.
        Assert.IsTrue(checkedCount > 0, "没有扫描到任何含非 ASCII 文本的 PowerShell 脚本，说明扫描逻辑已经失效。");
        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    private static IEnumerable<string> EnumeratePowerShellScripts()
    {
        var root = FindRepositoryRoot();
        Assert.IsNotNull(root, "找不到仓库根目录，无法扫描 PowerShell 脚本。");

        return Directory
            .EnumerateFiles(root!, "*.ps1", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindRepositoryRoot()
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

        return null;
    }
}
