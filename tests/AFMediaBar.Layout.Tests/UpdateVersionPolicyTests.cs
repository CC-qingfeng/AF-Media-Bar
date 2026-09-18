using System;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 版本号解析与比较策略的纯逻辑测试。
/// Pure-logic tests for the version parsing and comparison policy.
/// </summary>
[TestClass]
public sealed class UpdateVersionPolicyTests
{
    [TestMethod]
    public void TryParse_AcceptsEverySpellingTheReleaseSideHasUsed()
    {
        Assert.IsTrue(UpdateVersionPolicy.TryParse("1.2.3", out var threeParts));
        Assert.AreEqual(new Version(1, 2, 3, 0), threeParts);

        Assert.IsTrue(UpdateVersionPolicy.TryParse("v1.2", out var twoParts));
        Assert.AreEqual(new Version(1, 2, 0, 0), twoParts);

        Assert.IsTrue(UpdateVersionPolicy.TryParse("  V1.2.3.4  ", out var fourParts));
        Assert.AreEqual(new Version(1, 2, 3, 4), fourParts);
    }

    [TestMethod]
    public void TryParse_IgnoresPreReleaseAndBuildMetadata()
    {
        // 预发布与构建元数据只说明"哪一次构建"，不参与谁更新的判断。
        // Pre-release and build metadata describe which build this is, never which version is newer.
        Assert.IsTrue(UpdateVersionPolicy.TryParse("1.2.3-beta.1", out var preRelease));
        Assert.AreEqual(new Version(1, 2, 3, 0), preRelease);

        Assert.IsTrue(UpdateVersionPolicy.TryParse("1.2.3+8f3ac1", out var withMetadata));
        Assert.AreEqual(new Version(1, 2, 3, 0), withMetadata);
    }

    [TestMethod]
    public void TryParse_RejectsGarbageInsteadOfGuessing()
    {
        foreach (var invalid in new[] { null, string.Empty, "   ", "abc", "1.-2", "1..2", "1.2.3.4.5", "v", "-1.2", "1.2.3.4x" })
        {
            Assert.IsFalse(
                UpdateVersionPolicy.TryParse(invalid, out _),
                $"'{invalid}' must not be treated as a version.");
        }
    }

    [TestMethod]
    public void Format_DropsTrailingZeroSegmentsButKeepsMajorMinorAndBuild()
    {
        // 期望值跟随后续的刻意改动：`MinimumDisplaySegments` 从 2 调到 3，界面因此显示 "1.2.0" 而不是 "1.2"，
        // 与项目文件里的 `<Version>1.2.0</Version>` 保持一致（改动本身由使用者提交，测试当时没有跟着改）。
        // The expectations follow a deliberate later change: `MinimumDisplaySegments` went from 2 to 3, so the interface shows "1.2.0" instead of "1.2",
        // matching `<Version>1.2.0</Version>` in the project file. That change was committed on its own and the test was not updated with it.
        Assert.AreEqual("1.1.1", UpdateVersionPolicy.Format(new Version(1, 1, 1, 0)));
        Assert.AreEqual("1.2.3", UpdateVersionPolicy.Format(new Version(1, 2, 3, 0)));
        Assert.AreEqual("1.2.0", UpdateVersionPolicy.Format(new Version(1, 2, 0, 0)));
        Assert.AreEqual("2.0.0", UpdateVersionPolicy.Format(new Version(2, 0, 0, 0)));
        Assert.AreEqual("1.2.3.4", UpdateVersionPolicy.Format(new Version(1, 2, 3, 4)));
        Assert.AreEqual(string.Empty, UpdateVersionPolicy.Format(null));
    }

    [TestMethod]
    public void IsUpdateAvailable_ComparesNumericallyNotTextually()
    {
        Assert.IsTrue(UpdateVersionPolicy.IsUpdateAvailable("1.1.1", "1.2.0"));
        Assert.IsTrue(UpdateVersionPolicy.IsUpdateAvailable("1.1.1", "1.1.10"), "1.1.10 必须比 1.1.9 新。");
        Assert.IsTrue(UpdateVersionPolicy.IsUpdateAvailable("1.2", "1.2.0.1"));
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("1.2.0", "1.1.1"), "运行版本更高时不得提示更新。");
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("1.1.1", "v1.1.1"));
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("1.2", "1.2.0"), "1.2 与 1.2.0 是同一个版本。");
    }

    [TestMethod]
    public void IsUpdateAvailable_RefusesToGuessOnUnparseableInput()
    {
        // 宁可提示"已是最新"，也不要因为一个写坏的版本号给用户一个永远无法完成的更新。
        // Claiming "up to date" beats offering an update that can never complete because of a malformed version.
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("1.1.1", "最新版"));
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("未知", "2.0"));
        Assert.IsFalse(UpdateVersionPolicy.IsUpdateAvailable("1.1.1", null));
    }

    [TestMethod]
    public void IsSameVersion_TreatsEquivalentSpellingsAsOneVersion()
    {
        Assert.IsTrue(UpdateVersionPolicy.IsSameVersion("1.2", "v1.2.0"));
        Assert.IsTrue(UpdateVersionPolicy.IsSameVersion("1.2.0", "1.2.0.0"));
        Assert.IsFalse(UpdateVersionPolicy.IsSameVersion("1.2.0", "1.2.1"));
        Assert.IsFalse(UpdateVersionPolicy.IsSameVersion(null, "1.2.0"));
    }

    [TestMethod]
    public void IsMandatory_HonoursBothTheFlagAndTheMinimumSupportedVersion()
    {
        Assert.IsTrue(UpdateVersionPolicy.IsMandatory("1.1.1", CreateManifest("1.2.0", mandatory: true)));
        Assert.IsTrue(UpdateVersionPolicy.IsMandatory("1.1.1", CreateManifest("1.2.0", minimumSupportedVersion: "1.2")));
        Assert.IsFalse(UpdateVersionPolicy.IsMandatory("1.3.0", CreateManifest("1.4.0", minimumSupportedVersion: "1.2")));
        Assert.IsFalse(UpdateVersionPolicy.IsMandatory("1.1.1", CreateManifest("1.2.0")));
        Assert.IsFalse(UpdateVersionPolicy.IsMandatory("1.1.1", null));
        Assert.IsFalse(
            UpdateVersionPolicy.IsMandatory("1.1.1", CreateManifest("1.2.0", minimumSupportedVersion: "未知")),
            "最低支持版本写坏时不得把更新变成强制更新。");
    }

    internal static UpdateManifest CreateManifest(
        string version,
        bool mandatory = false,
        string? minimumSupportedVersion = null,
        IReadOnlyList<UpdatePackageAsset>? packages = null,
        IReadOnlyList<string>? accelerators = null)
    {
        return new UpdateManifest(
            version,
            ReleaseDate: null,
            Title: null,
            Changelog: [],
            ReleaseNotesUrl: null,
            ReleasePageUrl: null,
            MinimumSupportedVersion: minimumSupportedVersion,
            Mandatory: mandatory,
            Packages: packages ?? [],
            Accelerators: accelerators,
            PortableDownloadUrl: null);
    }
}
