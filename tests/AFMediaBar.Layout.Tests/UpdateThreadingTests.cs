using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 更新检查的线程契约：它在后台等待网络，回来后写设置，而设置写入会同步通知订阅者——其中包含绑定到界面的集合。
/// 第一次写发生在非 UI 线程时，WPF 的 <c>CollectionView</c> 会抛
/// “该类型的 CollectionView 不支持从调度程序线程以外的线程对其 SourceCollection 进行的更改”，
/// 异常随后以 <c>UnobservedTaskException</c> 的形式落进崩溃日志（真实报障就是这么来的）。
/// Threading contract of the update check: it waits for the network in the background and writes the settings afterwards, and a
/// settings write notifies its subscribers synchronously — including collections bound to the interface. The first write happening
/// off the UI thread makes WPF's <c>CollectionView</c> throw "this type of CollectionView does not support changes to its
/// SourceCollection from a thread different from the Dispatcher thread", and the exception then reaches the crash log as an
/// <c>UnobservedTaskException</c>, which is exactly how this was reported.
/// </summary>
[TestClass]
public sealed class UpdateThreadingTests
{
    [TestMethod]
    public void UpdateCheckWritesSettingsOnTheDispatcherThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    private static void Run()
    {
        var uiThreadId = Environment.CurrentManagedThreadId;

        // 订阅者里包含一个绑定到界面的集合：它的默认视图在 UI 线程上创建，因此从别的线程改它必然被拒。
        // One subscriber owns a collection bound to the interface: its default view is created on the UI thread, so mutating it from
        // any other thread is refused.
        var bound = new ObservableCollection<string>();
        _ = CollectionViewSource.GetDefaultView(bound);
        var handlerRuns = 0;
        var handlerOnUiThread = true;

        void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        {
            if (Interlocked.Increment(ref handlerRuns) == 1)
                handlerOnUiThread = Environment.CurrentManagedThreadId == uiThreadId;

            bound.Clear();
            bound.Add(e.PropertyName ?? string.Empty);
        }

        var previousUpdate = SettingsManager.Current.Update;
        var previousOverride = Environment.GetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable);
        SettingsManager.SettingsChanged += OnSettingsChanged;
        UpdateService? service = null;
        var storeDirectory = Path.Combine(Path.GetTempPath(), "AFMediaBarTests", Guid.NewGuid().ToString("N"));

        try
        {
            // 清单读取指向一个不存在的本地文件：不需要网络就能走到"写设置"那一步。
            // The manifest read points at a missing local file, so the settings write is reached without any network.
            Environment.SetEnvironmentVariable(
                UpdateManifestClient.ManifestUrlOverrideVariable,
                Path.Combine(storeDirectory, "no-such-manifest.json"));

            var store = new UpdatePackageStore(storeDirectory);
            service = new UpdateService(
                new UpdateManifestClient(),
                new UpdatePackageDownloader(store),
                store,
                new InstalledApplicationProbe(),
                new InstallCoordinatorMutex());

            Exception? thrown = null;
            var task = Task.Run(async () =>
            {
                try
                {
                    await service.CheckAsync(manual: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                Pump(25);

            var observed = SettingsManager.Current.Update;

            Assert.IsTrue(task.IsCompleted, "the update check must finish");
            Assert.IsNull(thrown, $"the update check must not throw: {thrown}");
            Assert.IsTrue(handlerRuns > 0, "the check must write the settings at least once");
            Assert.IsTrue(handlerOnUiThread, "the settings write must happen on the dispatcher thread");
            Assert.AreEqual(uiThreadId, Thread.CurrentThread.ManagedThreadId);
            Assert.AreEqual(1, bound.Count, "a bound collection must survive the settings change");
            Assert.IsFalse(observed.LastCheckSucceeded, "a failed manifest read must be recorded");
        }
        finally
        {
            service?.Dispose();
            SettingsManager.SettingsChanged -= OnSettingsChanged;
            SettingsManager.Current.Update = previousUpdate;
            Environment.SetEnvironmentVariable(UpdateManifestClient.ManifestUrlOverrideVariable, previousOverride);
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Normal,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
