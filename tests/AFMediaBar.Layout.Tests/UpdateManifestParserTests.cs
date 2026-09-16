using System;
using System.Linq;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 版本清单解析策略的纯逻辑测试：合法清单、缺字段、越界 schema、以及"哪些安装包条目可以被自动安装"。
/// Pure-logic tests for the manifest parsing policy: valid manifests, missing fields, forward schema versions,
/// and which installer entries may be installed automatically.
/// </summary>
[TestClass]
public sealed class UpdateManifestParserTests
{
    private const string ValidSha = "812f7f61c772b5f4e1e11a4663177f3b2944a68402919eec56602818039d141e";
    private const string OtherSha = "60d3d7cbd7656f1b25316d7c0c7e1f28c4a05039bff1a54dc886e570666353c2";

    [TestMethod]
    public void Parse_ReadsEveryFieldTheApplicationUses()
    {
        var json = """
        {
          "schemaVersion": 1,
          "version": "1.2.0",
          "releaseDate": "2026-09-16",
          "minimumSupportedVersion": "1.0.0",
          "mandatory": true,
          "title": "AF Media Bar 1.2.0",
          "changelog": ["  新增安装程序  ", "", "更新下载器"],
          "releaseNotesUrl": "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.2.0",
          "releasePageUrl": "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/latest",
          "packages": [
            { "url": "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/download/v1.2.0/AFMediaBar-Setup-v1.2.0-win-x64.exe",
              "size": 72200000, "sha256": "812F7F61C772B5F4E1E11A4663177F3B2944A68402919EEC56602818039D141E" }
          ],
          "accelerators": ["https://ghfast.top", "https://gh-proxy.com/"],
          "downloads": { "github": "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/download/v1.2.0/AFMediaBar-v1.2.0-win-x64.zip" }
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        var manifest = result.Manifest!;
        Assert.AreEqual("1.2.0", manifest.Version);
        Assert.AreEqual(new DateOnly(2026, 9, 16), manifest.ReleaseDate);
        Assert.AreEqual("1.0.0", manifest.MinimumSupportedVersion);
        Assert.IsTrue(manifest.Mandatory);
        Assert.AreEqual("AF Media Bar 1.2.0", manifest.Title);
        CollectionAssert.AreEqual(new[] { "新增安装程序", "更新下载器" }, manifest.Changelog.ToArray());
        Assert.AreEqual(1, manifest.Packages.Count);
        Assert.AreEqual(ValidSha, manifest.Packages[0].Sha256, "哈希统一按小写保存。");
        Assert.AreEqual(72200000, manifest.Packages[0].Size);
        CollectionAssert.AreEqual(
            new[] { "https://ghfast.top/", "https://gh-proxy.com/" },
            manifest.Accelerators!.ToArray(),
            "加速模板缺少尾斜杠时补齐，主机名统一小写。");
        StringAssert.EndsWith(manifest.PortableDownloadUrl!, ".zip");
    }

    [TestMethod]
    public void Parse_ToleratesAManifestWithoutSchemaVersion()
    {
        var result = UpdateManifestParser.Parse("""{ "version": "1.2.0" }""");

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        Assert.AreEqual("1.2.0", result.Manifest!.Version);
    }

    [TestMethod]
    public void Parse_ReportsEveryUnusableManifest()
    {
        Assert.IsFalse(UpdateManifestParser.Parse(null).Succeeded);
        Assert.IsFalse(UpdateManifestParser.Parse("   ").Succeeded);
        Assert.IsFalse(UpdateManifestParser.Parse("{not json").Succeeded);
        Assert.IsFalse(UpdateManifestParser.Parse("[1,2,3]").Succeeded);
        Assert.IsFalse(UpdateManifestParser.Parse("""{ "schemaVersion": 1 }""").Succeeded, "缺少 version 必须失败。");

        var forwardSchema = UpdateManifestParser.Parse("""{ "schemaVersion": 99, "version": "9.0.0" }""");
        Assert.IsFalse(forwardSchema.Succeeded);
        StringAssert.Contains(forwardSchema.FailureReason!, "99", "失败原因要说明清单要求了更高的 schema。");

        Assert.IsFalse(
            UpdateManifestParser.Parse("""{ "schemaVersion": 1, "version": "最新版" }""").Succeeded,
            "无法解析的版本号必须失败，而不是让比较退化。");
    }

    [TestMethod]
    public void Parse_DropsPackageEntriesThatCannotBeInstalledAutomatically()
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "version": "1.2.0",
          "packages": [
            { "url": "https://pan.quark.cn/s/6987e4945b16", "size": 100, "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.zip", "size": 100, "sha256": "{{ValidSha}}" },
            { "url": "http://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 100, "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 100 },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 100, "sha256": "abc" }
          ]
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        Assert.AreEqual(0, result.Manifest!.Packages.Count, "只有带完整哈希的 .exe 直链可以自动安装。");
    }

    [TestMethod]
    public void Parse_AcceptsEntriesWhoseDeclaredLengthIsMissingOrUnusable()
    {
        // 长度只用来人工核对，因此它缺失、为 0 或为负都不再让条目失效——真正决定成败的是哈希。
        // The length exists only for manual cross-checking, so a missing, zero or negative one no longer invalidates
        // an entry; the hash is what decides.
        var json = $$"""
        {
          "version": "1.2.0",
          "packages": [
            { "url": "https://github.com/o/r/releases/download/v1.2.0/a.exe", "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/b.exe", "size": 0, "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/c.exe", "size": -5, "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/d.exe", "size": "很大", "sha256": "{{ValidSha}}" }
          ]
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        Assert.AreEqual(4, result.Manifest!.Packages.Count);
        foreach (var package in result.Manifest.Packages)
        {
            Assert.IsNull(package.Size, "不可用的 length 一律按未声明处理。");
        }
    }

    [TestMethod]
    public void Parse_KeepsAUsableDeclaredLengthForManualCrossChecking()
    {
        var json = $$"""
        {
          "version": "1.2.0",
          "packages": [
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 72289314, "sha256": "{{ValidSha}}" }
          ]
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.AreEqual(72289314, result.Manifest!.Packages[0].Size);
    }

    [TestMethod]
    public void Parse_DropsMirrorsThatDisagreeOnTheHash()
    {
        // 镜像必须描述同一个文件：哈希不同（或两边都声明了长度却不同）的条目会被丢弃，
        // 否则一次下载可能装上一个清单从未承诺过的文件。
        // Mirrors must describe the same file: an entry with a different hash, or with a different declared length
        // when both declare one, is dropped, otherwise a download could install a file the manifest never vouched for.
        var json = $$"""
        {
          "version": "1.2.0",
          "packages": [
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 100, "sha256": "{{ValidSha}}" },
            { "url": "https://mirror.example/x.exe", "size": 100, "sha256": "{{OtherSha}}" },
            { "url": "https://mirror.example/y.exe", "size": 200, "sha256": "{{ValidSha}}" },
            { "url": "https://mirror.example/z.exe", "size": 100, "sha256": "{{ValidSha}}" }
          ]
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        CollectionAssert.AreEqual(
            new[] { "x.exe", "z.exe" },
            result.Manifest!.Packages.Select(package => Path.GetFileName(new Uri(package.Url).LocalPath)).ToArray());
    }

    [TestMethod]
    public void Parse_DeduplicatesRepeatedPackageUrls()
    {
        var json = $$"""
        {
          "version": "1.2.0",
          "packages": [
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 10, "sha256": "{{ValidSha}}" },
            { "url": "https://github.com/o/r/releases/download/v1.2.0/x.exe", "size": 10, "sha256": "{{OtherSha}}" }
          ]
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.AreEqual(1, result.Manifest!.Packages.Count);
        Assert.AreEqual(ValidSha, result.Manifest.Packages[0].Sha256, "重复地址保留第一条。");
    }

    [TestMethod]
    public void Parse_AcceleratorsDistinguishAbsentEmptyAndBroken()
    {
        // 字段缺失 → 使用内置默认列表；空数组 → 明确关闭加速；全部写坏 → 退回内置默认，而不是悄悄失去加速。
        // Missing keeps the built-in defaults, an empty array disables acceleration, and an entirely broken array
        // falls back to the defaults instead of silently losing acceleration.
        Assert.IsNull(UpdateManifestParser.Parse("""{ "version": "1.2.0" }""").Manifest!.Accelerators);
        Assert.AreEqual(0, UpdateManifestParser.Parse("""{ "version": "1.2.0", "accelerators": [] }""").Manifest!.Accelerators!.Count);
        Assert.IsNull(UpdateManifestParser.Parse("""{ "version": "1.2.0", "accelerators": ["not a url", "ftp://x/"] }""").Manifest!.Accelerators);
        Assert.IsNull(UpdateManifestParser.Parse("""{ "version": "1.2.0", "accelerators": "https://ghfast.top/" }""").Manifest!.Accelerators);

        var mixed = UpdateManifestParser.Parse(
            """{ "version": "1.2.0", "accelerators": ["https://ghfast.top/", "https://host/path/", 7, "https://GHPROXY.NET"] }""");
        CollectionAssert.AreEqual(
            new[] { "https://ghfast.top/", "https://ghproxy.net/" },
            mixed.Manifest!.Accelerators!.ToArray(),
            "带路径的模板被丢弃；重复主机只保留一条。");
    }

    [TestMethod]
    public void Parse_AllowsPlainHttpOnlyOnTheLoopbackAddress()
    {
        // 回环例外只为验收存在：本地清单 + 本地服务器可以走完整链路，而不必先伪造受信任证书。
        // The loopback exception exists for acceptance: a local manifest plus a local server can drive the whole
        // chain without forging a trusted certificate first.
        var loopback = UpdateManifestParser.Parse(
            """{ "version": "1.2.0", "packages": [ { "url": "http://127.0.0.1:8080/x.exe", "size": 10, "sha256": "812f7f61c772b5f4e1e11a4663177f3b2944a68402919eec56602818039d141e" } ] }""");
        Assert.AreEqual(1, loopback.Manifest!.Packages.Count);

        var remote = UpdateManifestParser.Parse(
            """{ "version": "1.2.0", "packages": [ { "url": "http://example.com/x.exe", "size": 10, "sha256": "812f7f61c772b5f4e1e11a4663177f3b2944a68402919eec56602818039d141e" } ] }""");
        Assert.AreEqual(0, remote.Manifest!.Packages.Count, "非回环的明文地址仍然必须被拒绝。");

        var page = UpdateManifestParser.Parse("""{ "version": "1.2.0", "releasePageUrl": "http://example.com/releases" }""");
        Assert.IsNull(page.Manifest!.ReleasePageUrl);
    }

    [TestMethod]
    public void ShippedManifestInTheRepositoryParsesWithTheApplicationParser()
    {
        // 仓库里的 docs/latest.json 就是程序运行时读取的那份文件。发布侧写坏它（多一个逗号、编码损坏、
        // schema 版本超出）不会有任何编译或发布步骤报错，只会让所有人的更新检查失败，因此这里直接解析真文件。
        // The repository's docs/latest.json is the file the application actually reads. A release-side mistake in it
        // (a stray comma, a broken encoding, a forward schema version) fails no build or publish step and would only
        // break every user's update check, so the real file is parsed here.
        var path = FindRepositoryFile(Path.Combine("docs", "latest.json"));
        Assert.IsNotNull(path, "docs/latest.json 必须存在于仓库中。");

        var result = UpdateManifestParser.Parse(File.ReadAllText(path!));

        Assert.IsTrue(result.Succeeded, $"已发布的清单必须能被程序解析：{result.FailureReason}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Manifest!.Version), "清单必须声明版本号。");
    }

    [TestMethod]
    public void ShippedManifestKeepsEveryPackageAtTheSameSizeAndHash()
    {
        var path = FindRepositoryFile(Path.Combine("docs", "latest.json"));
        Assert.IsNotNull(path);

        var manifest = UpdateManifestParser.Parse(File.ReadAllText(path!)).Manifest!;
        foreach (var package in manifest.Packages)
        {
            Assert.AreEqual(manifest.Packages[0].Size, package.Size, "packages[] 的每个条目都必须描述同一个安装包。");
            Assert.AreEqual(
                manifest.Packages[0].Sha256,
                package.Sha256,
                "packages[] 的每个条目都必须共享同一份哈希，否则哈希保护会随镜像而变。");
        }
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [TestMethod]
    public void Parse_CapsTheChangelogSoTheSettingsPageStaysBounded()
    {
        var entries = string.Join(',', Enumerable.Range(0, 40).Select(index => $"\"第 {index} 条\""));
        var result = UpdateManifestParser.Parse($$"""{ "version": "1.2.0", "changelog": [{{entries}}] }""");

        Assert.AreEqual(UpdateManifestParser.MaximumChangelogEntries, result.Manifest!.Changelog.Count);
    }

    [TestMethod]
    public void Parse_IgnoresUnknownFieldsAndBrokenOptionalValues()
    {
        var json = """
        {
          "version": "1.2.0",
          "releaseDate": "不是日期",
          "title": 42,
          "releaseNotesUrl": "javascript:alert(1)",
          "downloads": { "github": "ftp://example.com/x.zip" },
          "futureField": { "anything": [1, 2, 3] }
        }
        """;

        var result = UpdateManifestParser.Parse(json);

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        Assert.IsNull(result.Manifest!.ReleaseDate);
        Assert.IsNull(result.Manifest.Title);
        Assert.IsNull(result.Manifest.ReleaseNotesUrl);
        Assert.IsNull(result.Manifest.PortableDownloadUrl);
    }
}
