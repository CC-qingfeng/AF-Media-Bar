using System;
using AFMediaBar.Classes.Services.Credits;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 名单的呈现规则：名字连成一段文本、头像地址的尺寸提示与缓存文件名。
///
/// 这些规则写错的后果分别是"同一个人显示两遍"、"头像请求打到不支持该参数的服务器上导致整批头像消失"、
/// 以及"同一张头像在磁盘缓存里被存成不同文件"，都属于用户看得见的错误，因此值得钉住。
/// These rules break into: one person shown twice, avatar requests hitting a server that rejects the parameter and losing every avatar, and one avatar
/// stored under several disk-cache names — all visible to the user, so they are worth pinning down.
/// </summary>
[TestClass]
public sealed class CreditsPresentationPolicyTests
{
    /// <summary>名字按给定连接符连成一段，顺序保持。/ Names are joined with the given separator, keeping their order.</summary>
    [TestMethod]
    public void NamesAreJoinedInOrderWithTheSeparator()
    {
        Assert.AreEqual("张三、李四、王五", CreditsPresentationPolicy.BuildNameList(["张三", "李四", "王五"], "、"));
        Assert.AreEqual("Ann, Bob", CreditsPresentationPolicy.BuildNameList(["Ann", "Bob"], ", "));
        Assert.AreEqual(string.Empty, CreditsPresentationPolicy.BuildNameList([], "、"));
    }

    /// <summary>空白项被丢掉、重复项去重（不区分大小写），并且两侧空白会被裁掉。/ Blank entries are dropped, duplicates are removed case-insensitively, and surrounding whitespace is trimmed.</summary>
    [TestMethod]
    public void BlankAndDuplicateNamesAreRemoved()
    {
        Assert.AreEqual(
            "Ann、Bob",
            CreditsPresentationPolicy.BuildNameList(["Ann", "  ", null, "ann", " Bob ", ""], "、"));
    }

    /// <summary>不修改传入的集合（名单来自快照，快照是不可变的）。/ The input is not modified, because the names come from an immutable snapshot.</summary>
    [TestMethod]
    public void TheSourceCollectionIsNotModified()
    {
        var names = new[] { "Ann", "Ann" };

        CreditsPresentationPolicy.BuildNameList(names, "、");

        Assert.AreEqual(2, names.Length);
    }

    /// <summary>
    /// GitHub 头像地址会带上尺寸提示，且不会重复添加；其它主机原样返回（避免未知参数把请求打坏），非法地址返回空。
    /// A GitHub avatar address gets the size hint and never gets it twice; other hosts are returned unchanged, so an unknown parameter cannot break the
    /// request, and an invalid address returns empty.
    /// </summary>
    [TestMethod]
    public void AvatarSizeHintOnlyAppliesToGitHubAvatarHosts()
    {
        Assert.AreEqual(
            "https://avatars.githubusercontent.com/u/123?v=4&s=64",
            CreditsPresentationPolicy.ResolveAvatarRequestUrl("https://avatars.githubusercontent.com/u/123?v=4"));
        Assert.AreEqual(
            "https://avatars.githubusercontent.com/u/123?s=64",
            CreditsPresentationPolicy.ResolveAvatarRequestUrl("https://avatars.githubusercontent.com/u/123"));
        Assert.AreEqual(
            "https://avatars.githubusercontent.com/u/123?s=64",
            CreditsPresentationPolicy.ResolveAvatarRequestUrl("https://avatars.githubusercontent.com/u/123?s=64"));
        Assert.AreEqual(
            "https://example.com/avatar.png",
            CreditsPresentationPolicy.ResolveAvatarRequestUrl("https://example.com/avatar.png"));
        Assert.AreEqual(string.Empty, CreditsPresentationPolicy.ResolveAvatarRequestUrl(null));
        Assert.AreEqual(string.Empty, CreditsPresentationPolicy.ResolveAvatarRequestUrl("not a url"));
        Assert.AreEqual(string.Empty, CreditsPresentationPolicy.ResolveAvatarRequestUrl("file:///C:/a.png"));
    }

    /// <summary>缓存文件名是稳定的十六进制摘要，同一地址永远同一个名字，且不含非法字符。/ The cache file name is a stable hexadecimal digest: one address always maps to one name, with no illegal characters.</summary>
    [TestMethod]
    public void CacheFileNamesAreStableHex()
    {
        var first = CreditsPresentationPolicy.ResolveCacheFileName("https://avatars.githubusercontent.com/u/1?v=4&s=64");
        var second = CreditsPresentationPolicy.ResolveCacheFileName("https://avatars.githubusercontent.com/u/1?v=4&s=64");
        var other = CreditsPresentationPolicy.ResolveCacheFileName("https://avatars.githubusercontent.com/u/2?v=4&s=64");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, other);
        Assert.AreEqual(64, first.Length);
        foreach (var character in first)
        {
            Assert.IsTrue(character is >= '0' and <= '9' or >= 'a' and <= 'f', $"不是十六进制字符：{character}");
        }
    }

    /// <summary>头像边长是一个有界取值：太小会糊，太大则白白多花流量。/ The avatar edge length is bounded: too small looks blurry and too large wastes traffic.</summary>
    [TestMethod]
    public void AvatarSizeStaysInASaneRange()
    {
        Assert.IsTrue(CreditsPresentationPolicy.AvatarPixelSize >= 32);
        Assert.IsTrue(CreditsPresentationPolicy.AvatarPixelSize <= 256);
    }
}
