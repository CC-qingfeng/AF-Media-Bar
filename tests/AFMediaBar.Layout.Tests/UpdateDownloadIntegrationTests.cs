using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 更新下载链路的本地回环集成测试：清单读取、下载、哈希校验与待安装记录。
///
/// 这里用 127.0.0.1 上的临时 TCP 服务器而不是真实地址：测试不依赖外网，也不依赖发布是否已经发生，
/// 但走的仍是真实的 HttpClient、文件写入与 SHA-256 路径，因此"哈希不符一定删除并且不写记录"这类
/// 结论是真正被执行过的。
/// Loopback integration tests for the update download chain: manifest reading, downloading, hash verification and
/// the pending record.
///
/// A temporary TCP server on 127.0.0.1 is used instead of real addresses: the tests need neither the internet nor
/// a published release, yet they still exercise the real HttpClient, file writing and SHA-256 paths, so claims such
/// as "a hash mismatch always deletes the file and writes no record" are actually executed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class UpdateDownloadIntegrationTests
{
    private string _directory = string.Empty;
    private string? _previousManifestOverride;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AFMediaBarUpdateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousManifestOverride = Environment.GetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable);
        Environment.SetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable, _previousManifestOverride);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [TestMethod]
    public async Task Download_WritesTheInstallerAndThePendingRecord()
    {
        var payload = CreatePayload(256 * 1024);
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/AFMediaBar-Setup-v1.2.0-win-x64.exe"] = LocalHttpServer.Route.Ok(payload)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/AFMediaBar-Setup-v1.2.0-win-x64.exe", payload.Length, Sha256(payload));
        var progress = new List<double>();

        var outcome = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            new Progress<double>(progress.Add),
            CancellationToken.None);

        Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
        Assert.IsTrue(File.Exists(outcome.FilePath));
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(outcome.FilePath!));
        Assert.AreEqual(UpdatePendingFileAction.Reuse, store.Evaluate(asset), "下载完成后必须可以直接复用。");

        var record = store.ReadPendingRecord();
        Assert.IsNotNull(record);
        Assert.AreEqual("1.2.0", record!.Version);
        Assert.AreEqual(asset.Sha256, record.Sha256);
        Assert.AreEqual(payload.Length, record.Size);
    }

    [TestMethod]
    public async Task Download_DeletesTheFileWhenTheHashDoesNotMatch()
    {
        var payload = CreatePayload(64 * 1024);
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/x.exe"] = LocalHttpServer.Route.Ok(payload)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/x.exe", payload.Length, Sha256(CreatePayload(64 * 1024)));

        var outcome = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            null,
            CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.StartsWith(outcome.FailureReason!, "安装包校验失败");
        Assert.IsFalse(File.Exists(Path.Combine(_directory, "x.exe")), "哈希不符的文件必须被删除。");
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length, "校验失败后不得留下 .part 文件或待安装记录。");
        Assert.IsNull(store.ReadPendingRecord());
    }

    [TestMethod]
    public async Task Download_IgnoresTheDeclaredLengthAndTrustsTheHash()
    {
        // 清单里的长度不参与校验：服务器给出的字节数与它不同时，只要哈希一致就照样接受，
        // 因此发布侧写错 size 不再会让更新彻底失败。
        // The declared length takes no part in verification: when the server returns a different number of bytes the
        // download is still accepted as long as the hash matches, so a wrong size on the release side can no longer
        // break the update outright.
        var payload = CreatePayload(32 * 1024);
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/x.exe"] = LocalHttpServer.Route.Ok(payload)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/x.exe", payload.Length - 1024, Sha256(payload));

        var outcome = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            null,
            CancellationToken.None);

        Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
        Assert.AreEqual(payload.Length, outcome.Record!.Size, "记录写的是实际收到的字节数，不是清单声明值。");
    }

    [TestMethod]
    public async Task Download_WorksWithoutADeclaredLength()
    {
        // size 是可选字段：没写时进度条拿不到百分比，但下载与校验照常完成。
        // size is optional: without it the progress bar has no percentage, while the download and its verification
        // still complete normally.
        var payload = CreatePayload(16 * 1024);
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/x.exe"] = LocalHttpServer.Route.Ok(payload)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/x.exe", null, Sha256(payload));

        var outcome = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            null,
            CancellationToken.None);

        Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
        Assert.AreEqual(UpdatePendingFileAction.Reuse, store.Evaluate(asset));
    }

    [TestMethod]
    public async Task Download_ReportsAFailedStatusWithoutWritingAnything()
    {
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/missing.exe"] = LocalHttpServer.Route.WithStatus(404)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/missing.exe", 10, new string('a', 64));

        var outcome = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            null,
            CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.FailureReason!, "404");
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task Download_ReVerifiesAFileWhoseTimestampChanged()
    {
        var payload = CreatePayload(48 * 1024);
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/x.exe"] = LocalHttpServer.Route.Ok(payload)
        });

        var store = new UpdatePackageStore(_directory);
        var downloader = new UpdatePackageDownloader(store);
        var asset = new UpdatePackageAsset(server.BaseUrl + "/x.exe", payload.Length, Sha256(payload));

        var first = await downloader.DownloadAsync(
            new UpdateDownloadSource(asset.Url, "127.0.0.1", false),
            asset,
            "1.2.0",
            null,
            CancellationToken.None);
        Assert.IsTrue(first.Succeeded, first.FailureReason);

        // 时间戳变了就必须重新校验，而不是相信磁盘上那份文件。
        // A changed timestamp forces re-verification instead of trusting the file on disk.
        File.SetLastWriteTimeUtc(first.FilePath!, DateTime.UtcNow.AddMinutes(5));
        Assert.AreEqual(UpdatePendingFileAction.VerifyAgain, store.Evaluate(asset));

        var verified = await downloader.VerifyAsync(first.FilePath!, asset, "1.2.0", null, CancellationToken.None);

        Assert.IsTrue(verified.Succeeded, verified.FailureReason);
        Assert.AreEqual(UpdatePendingFileAction.Reuse, store.Evaluate(asset));
    }

    [TestMethod]
    public async Task ManifestClient_ReadsALocalManifestThroughTheOverride()
    {
        var manifestPath = Path.Combine(_directory, "latest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "schemaVersion": 1,
              "version": "1.2.0",
              "packages": [
                { "url": "https://github.com/o/r/releases/download/v1.2.0/AFMediaBar-Setup-v1.2.0-win-x64.exe",
                  "size": 1024, "sha256": "812f7f61c772b5f4e1e11a4663177f3b2944a68402919eec56602818039d141e" }
              ]
            }
            """);
        Environment.SetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable, manifestPath);

        var result = await new UpdateManifestClient().FetchAsync("AFMediaBar/1.1.1", CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.FailureReason);
        Assert.AreEqual("1.2.0", result.Manifest!.Version);
        Assert.AreEqual(manifestPath, result.Endpoint);

        var plan = UpdateSourcePlanPolicy.BuildDownloadPlan(result.Manifest);
        Assert.IsTrue(plan.Count >= 1);
        Assert.AreEqual("github.com", plan[0].HostName);
    }

    [TestMethod]
    public async Task ManifestClient_ReportsAnHttpFailureWithoutThrowing()
    {
        using var server = new LocalHttpServer(new Dictionary<string, LocalHttpServer.Route>
        {
            ["/latest.json"] = LocalHttpServer.Route.WithStatus(404)
        });
        Environment.SetEnvironmentVariable(
            UpdateManifestClient.ManifestUrlOverrideVariable,
            server.BaseUrl + "/latest.json");

        var result = await new UpdateManifestClient().FetchAsync("AFMediaBar/1.1.1", CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureReason!, "404");
    }

    [TestMethod]
    public void ManifestEndpoints_AreOrderedWithRawFirstAndNoProxy()
    {
        var endpoints = UpdateSourcePlanPolicy.ManifestEndpoints;

        Assert.IsTrue(endpoints.Count >= 2);
        StringAssert.StartsWith(endpoints[0], "https://raw.githubusercontent.com/");
        StringAssert.Contains(endpoints[1], "cdn.jsdelivr.net");

        // 清单端点绝不能是第三方加速站点：安装包有清单里的哈希兜底，清单本身没有。
        // A manifest endpoint must never be a third-party accelerator: an installer is backstopped by the hash in
        // the manifest, the manifest is not.
        foreach (var endpoint in endpoints)
        {
            foreach (var accelerator in GitHubAcceleratorPolicy.DefaultAccelerators)
            {
                Assert.IsFalse(
                    endpoint.StartsWith(accelerator, StringComparison.OrdinalIgnoreCase),
                    $"清单端点不得使用加速站点：{endpoint}");
            }
        }
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    private static string Sha256(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    /// <summary>
    /// 只够用的本地 HTTP 服务器：按路径返回固定响应。
    ///
    /// 用 TcpListener 而不是 HttpListener，因为 HttpListener 在非管理员账户下需要 URL ACL，而这里只服务
    /// 127.0.0.1 上的固定路径，没有必要引入那层依赖。
    /// A minimal local HTTP server returning a fixed response per path.
    ///
    /// TcpListener is used rather than HttpListener because HttpListener needs a URL ACL without administrator
    /// rights, and serving fixed paths on 127.0.0.1 needs no such dependency.
    /// </summary>
    private sealed class LocalHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, Route> _routes;
        private readonly CancellationTokenSource _shutdown = new();

        public LocalHttpServer(Dictionary<string, Route> routes)
        {
            _routes = routes;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        public void Dispose()
        {
            _shutdown.Cancel();
            _listener.Stop();
            _shutdown.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => RespondAsync(client));
            }
        }

        private async Task RespondAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync();
                    var path = requestLine?.Split(' ') is { Length: >= 2 } parts ? parts[1] : string.Empty;
                    while (true)
                    {
                        var header = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(header))
                        {
                            break;
                        }
                    }

                    var route = _routes.TryGetValue(path, out var found) ? found : Route.WithStatus(404);
                    var body = route.Body ?? [];
                    var reason = route.Status == 200 ? "OK" : route.Status == 404 ? "Not Found" : "Error";
                    var headerText = $"HTTP/1.1 {route.Status} {reason}\r\n" +
                                     $"Content-Length: {body.Length}\r\n" +
                                     "Content-Type: application/octet-stream\r\n" +
                                     "Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headerText));
                    if (body.Length > 0)
                    {
                        await stream.WriteAsync(body);
                    }

                    await stream.FlushAsync();
                }
                catch (Exception)
                {
                    // 测试客户端可能已经关闭连接；这里不需要报告。
                    // The test client may already have closed the connection, which needs no reporting.
                }
            }
        }

        internal sealed record Route(int Status, byte[]? Body)
        {
            public static Route Ok(byte[] body) => new(200, body);

            public static Route WithStatus(int status) => new(status, []);
        }
    }
}
