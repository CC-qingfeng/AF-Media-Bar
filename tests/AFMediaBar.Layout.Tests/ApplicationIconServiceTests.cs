using System;
using System.IO;
using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 音量合成器的应用图标：必须交回像素正确的图像（PNG，带真实透明度），而不是带 AND 掩码的 DIB——
/// 后者会让 WPF 解出的图标发灰、偏色（用户反馈"图标颜色怪怪的"）。
/// Mixer application icons: the pipeline has to hand back a pixel-correct image (PNG with real transparency) instead of a DIB carrying an
/// AND mask, which WPF decodes as grey and colour-cast icons, the reported "the icons look off".
/// </summary>
[TestClass]
public sealed class ApplicationIconServiceTests
{
    [TestMethod]
    public void AssociatedExecutableIconsAreReturnedAsPng()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            Assert.Inconclusive("当前环境拿不到测试宿主可执行文件 / the test host executable is unavailable in this environment");
            return;
        }

        var service = new ApplicationIconService(new AudioProcessInfoService());
        var data = service.GetIconData((uint)Environment.ProcessId, executable);

        Assert.IsNotNull(data, "可执行文件应当能取到图标 / an executable is expected to yield an icon");
        Assert.IsTrue(data.Length > 8);
        // PNG 签名 89 50 4E 47：证明交回的是合成好的 32bpp ARGB 图像，而不是掩码 DIB。
        // The PNG signature 89 50 4E 47 proves a composited 32bpp ARGB image is returned rather than a mask DIB.
        Assert.AreEqual(0x89, data[0]);
        Assert.AreEqual(0x50, data[1]);
        Assert.AreEqual(0x4E, data[2]);
        Assert.AreEqual(0x47, data[3]);
    }

    [TestMethod]
    public void MissingFilesYieldNoIcon()
    {
        var service = new ApplicationIconService(new AudioProcessInfoService());
        var missing = Path.Combine(Path.GetTempPath(), $"afmb-missing-icon-{Guid.NewGuid():N}.exe");

        // 进程号取不到可执行文件、会话路径也不存在时，唯一的结果是"没有图标"（不是抛异常，也不是别的进程的图标）。
        // With no executable for the process id and a session path that does not exist, the only outcome is "no icon" — not an exception and
        // not another process's icon.
        Assert.IsNull(service.GetIconData(uint.MaxValue, missing));
    }
}
