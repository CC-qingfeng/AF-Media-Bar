using System;
using System.Collections.Generic;
using System.Linq;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 下载渠道编排策略的纯逻辑测试：加速候选展开、尝试顺序、以及已下载安装包的复用判断。
/// Pure-logic tests for download-channel orchestration: accelerator expansion, attempt order, and the reuse
/// decision for an already downloaded installer.
/// </summary>
[TestClass]
public sealed class UpdateDownloadPolicyTests
{
    private const string InstallerUrl =
        "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/download/v1.2.0/AFMediaBar-Setup-v1.2.0-win-x64.exe";

    private const string Sha256A = "812f7f61c772b5f4e1e11a4663177f3b2944a68402919eec56602818039d141e";
    private const string Sha256B = "60d3d7cbd7656f1b25316d7c0c7e1f28c4a05039bff1a54dc886e570666353c2";

    [TestMethod]
    public void CanAccelerate_OnlyAcceptsPlainGitHubHttpsLinks()
    {
        Assert.IsTrue(GitHubAcceleratorPolicy.CanAccelerate(InstallerUrl));
        Assert.IsTrue(GitHubAcceleratorPolicy.CanAccelerate("https://GITHUB.com/o/r/releases/latest"));

        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate("http://github.com/o/r/x.exe"), "只接受 https。");
        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate("https://raw.githubusercontent.com/o/r/main/docs/latest.json"));
        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate("https://example.com/github.com/x.exe"));
        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate(null));
    }

    [TestMethod]
    public void CanAccelerate_RefusesToRewriteAnAlreadyRewrittenAddress()
    {
        // 二次改写会把两个站点串起来，结果通常是 404。
        // Rewriting twice chains two sites together and usually ends in a 404.
        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate($"https://ghfast.top/{InstallerUrl}"));
        Assert.IsFalse(GitHubAcceleratorPolicy.CanAccelerate("https://ghfast.top/https://github.com/o/r/x.exe"));
    }

    [TestMethod]
    public void Expand_UsesTemplateOrderAndNeverReturnsTheDirectLink()
    {
        var candidates = GitHubAcceleratorPolicy.Expand(InstallerUrl, accelerators: null);

        Assert.AreEqual(GitHubAcceleratorPolicy.DefaultAccelerators.Count, candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            Assert.AreEqual(
                GitHubAcceleratorPolicy.DefaultAccelerators[index] + InstallerUrl,
                candidates[index],
                "候选顺序必须与模板顺序一致。");
        }

        Assert.IsFalse(candidates.Contains(InstallerUrl), "展开结果只包含加速地址，直链由调用方单独排在前面。");
    }

    [TestMethod]
    public void Expand_HonoursAnOverrideAndAnEmptyList()
    {
        var custom = GitHubAcceleratorPolicy.Expand(InstallerUrl, ["https://mirror.example", "https://ghfast.top/"]);
        CollectionAssert.AreEqual(
            new[] { $"https://mirror.example/{InstallerUrl}", $"https://ghfast.top/{InstallerUrl}" },
            custom.ToArray());

        Assert.AreEqual(0, GitHubAcceleratorPolicy.Expand(InstallerUrl, []).Count, "空数组表示明确关闭加速。");
        Assert.AreEqual(0, GitHubAcceleratorPolicy.Expand("https://example.com/x.exe", null).Count);
    }

    [TestMethod]
    public void Expand_SkipsBrokenTemplatesAndTheHostsOwnTemplate()
    {
        var candidates = GitHubAcceleratorPolicy.Expand(
            InstallerUrl,
            ["not a url", "https://host/path/", "https://github.com/", "https://ghfast.top", "https://ghfast.top/"]);

        CollectionAssert.AreEqual(new[] { $"https://ghfast.top/{InstallerUrl}" }, candidates.ToArray());
    }

    [TestMethod]
    public void NormalizeTemplate_RejectsAnythingThatIsNotAHostRoot()
    {
        Assert.AreEqual("https://ghfast.top/", GitHubAcceleratorPolicy.NormalizeTemplate("  https://GHFAST.top  "));
        Assert.AreEqual("https://ghfast.top/", GitHubAcceleratorPolicy.NormalizeTemplate("https://ghfast.top/"));
        Assert.AreEqual(string.Empty, GitHubAcceleratorPolicy.NormalizeTemplate("https://ghfast.top/gh/"));
        Assert.AreEqual(string.Empty, GitHubAcceleratorPolicy.NormalizeTemplate("https://ghfast.top/?x=1"));
        Assert.AreEqual(string.Empty, GitHubAcceleratorPolicy.NormalizeTemplate("http://ghfast.top/"));
        Assert.AreEqual(string.Empty, GitHubAcceleratorPolicy.NormalizeTemplate("ghfast.top"));
        Assert.AreEqual(string.Empty, GitHubAcceleratorPolicy.NormalizeTemplate(null));
    }

    [TestMethod]
    public void BuildDownloadPlan_PutsEachDirectLinkBeforeItsOwnAcceleratedCandidates()
    {
        var manifest = UpdateVersionPolicyTests.CreateManifest(
            "1.2.0",
            packages: [new UpdatePackageAsset(InstallerUrl, 72200000, Sha256A)],
            accelerators: ["https://ghfast.top/", "https://gh-proxy.com/"]);

        var plan = UpdateSourcePlanPolicy.BuildDownloadPlan(manifest);

        Assert.AreEqual(3, plan.Count);
        Assert.AreEqual(InstallerUrl, plan[0].Url);
        Assert.IsFalse(plan[0].IsAccelerated);
        Assert.AreEqual("github.com", plan[0].HostName);

        Assert.IsTrue(plan[1].IsAccelerated);
        Assert.AreEqual("ghfast.top", plan[1].HostName);
        Assert.IsTrue(plan[2].IsAccelerated);
        Assert.AreEqual("gh-proxy.com", plan[2].HostName);
    }

    [TestMethod]
    public void BuildDownloadPlan_IsEmptyWithoutAPackageOrWithAccelerationDisabled()
    {
        Assert.AreEqual(0, UpdateSourcePlanPolicy.BuildDownloadPlan(null).Count);
        Assert.AreEqual(0, UpdateSourcePlanPolicy.BuildDownloadPlan(UpdateVersionPolicyTests.CreateManifest("1.2.0")).Count);

        var directOnly = UpdateSourcePlanPolicy.BuildDownloadPlan(UpdateVersionPolicyTests.CreateManifest(
            "1.2.0",
            packages: [new UpdatePackageAsset(InstallerUrl, 72200000, Sha256A)],
            accelerators: []));
        Assert.AreEqual(1, directOnly.Count);
        Assert.IsFalse(directOnly[0].IsAccelerated);
    }

    [TestMethod]
    public void SelectNext_SkipsFailedSourcesAndMovesToManualDownloadWhenExhausted()
    {
        var plan = UpdateSourcePlanPolicy.BuildDownloadPlan(UpdateVersionPolicyTests.CreateManifest(
            "1.2.0",
            packages: [new UpdatePackageAsset(InstallerUrl, 72200000, Sha256A)],
            accelerators: ["https://ghfast.top/"]));

        Assert.AreEqual(InstallerUrl, UpdateSourcePlanPolicy.SelectNext(plan, null)!.Url);
        Assert.IsFalse(UpdateSourcePlanPolicy.IsManualOnly(plan, null));

        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InstallerUrl };
        Assert.AreEqual("ghfast.top", UpdateSourcePlanPolicy.SelectNext(plan, attempted)!.HostName);

        attempted.Add($"https://ghfast.top/{InstallerUrl}");
        Assert.IsNull(UpdateSourcePlanPolicy.SelectNext(plan, attempted));
        Assert.IsTrue(UpdateSourcePlanPolicy.IsManualOnly(plan, attempted));
    }

    [TestMethod]
    public void PendingFile_DownloadsVerifiesAgainReusesOrDiscards()
    {
        var asset = new UpdatePackageAsset(InstallerUrl, 72200000, Sha256A);
        var modified = new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
        var record = new UpdatePendingFileRecord(@"C:\updates\x.exe", "1.2.0", Sha256A, 72200000, modified);

        Assert.AreEqual(
            UpdatePendingFileAction.Download,
            UpdatePendingFilePolicy.Decide(record, asset, fileExists: false, 72200000, modified));
        Assert.AreEqual(
            UpdatePendingFileAction.Download,
            UpdatePendingFilePolicy.Decide(null, asset, fileExists: true, 72200000, modified));
        Assert.AreEqual(
            UpdatePendingFileAction.Discard,
            UpdatePendingFilePolicy.Decide(record, asset with { Sha256 = Sha256B }, true, 72200000, modified),
            "哈希与清单不同时只能丢弃：完整性只有哈希能证明。");
        Assert.AreEqual(
            UpdatePendingFileAction.Reuse,
            UpdatePendingFilePolicy.Decide(record, asset, true, 72200000, modified),
            "哈希与本机大小、时间戳都没变时可以跳过重算 70 MB 的哈希。");
        Assert.AreEqual(
            UpdatePendingFileAction.VerifyAgain,
            UpdatePendingFilePolicy.Decide(record, asset, true, 1, modified),
            "本机文件长度变了就重新哈希确认，而不是直接相信它。");
        Assert.AreEqual(
            UpdatePendingFileAction.VerifyAgain,
            UpdatePendingFilePolicy.Decide(record, asset, true, 72200000, modified.AddMinutes(5)));
    }

    [TestMethod]
    public void PendingFile_IgnoresTheManifestsDeclaredLength()
    {
        // 清单里的长度只是人工核对用的信息：它既不参与判断，写错也不该让一个已校验过的文件被丢弃。
        // The length in the manifest is informational: it decides nothing, and a wrong one must not discard a file
        // that already passed verification.
        var modified = new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
        var record = new UpdatePendingFileRecord(@"C:\updates\x.exe", "1.2.0", Sha256A, 72200000, modified);

        Assert.AreEqual(
            UpdatePendingFileAction.Reuse,
            UpdatePendingFilePolicy.Decide(
                record,
                new UpdatePackageAsset(InstallerUrl, 1, Sha256A),
                true,
                72200000,
                modified));

        Assert.AreEqual(
            UpdatePendingFileAction.Reuse,
            UpdatePendingFilePolicy.Decide(
                record,
                new UpdatePackageAsset(InstallerUrl, null, Sha256A),
                true,
                72200000,
                modified));
    }

    [TestMethod]
    public void CreateRecord_NormalizesTheHashCaseAndKeepsTheReceivedLength()
    {
        var record = UpdatePendingFilePolicy.CreateRecord(
            @"C:\updates\x.exe",
            "1.2.0",
            new UpdatePackageAsset(InstallerUrl, 10, Sha256A.ToUpperInvariant()),
            72289314,
            DateTimeOffset.UnixEpoch);

        Assert.AreEqual(Sha256A, record.Sha256);
        Assert.AreEqual("1.2.0", record.Version);
        Assert.AreEqual(72289314, record.Size, "记录里的长度是本机实际收到的字节数，而不是清单声明值。");
    }
}
