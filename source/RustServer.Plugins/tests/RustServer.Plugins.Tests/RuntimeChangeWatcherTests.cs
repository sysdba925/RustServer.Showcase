using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeChangeWatcherTests
{
    [Fact]
    public async Task NotifiesOnlyForManagedRuntimePaths()
    {
        using var runtimeRoot = new TemporaryRuntimeRoot();
        Directory.CreateDirectory(runtimeRoot.ServerRootPath);
        using var notifier = new RuntimeChangeNotifier();
        using var subscription = notifier.Subscribe();
        var watcher = new RuntimeChangeWatcher(
            Options.Create(new RuntimeOptions { ServerRootPath = runtimeRoot.ServerRootPath }),
            new ManagedFileMapping(),
            notifier,
            NullLogger<RuntimeChangeWatcher>.Instance);

        watcher.ObservePath(Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs"));

        using var managedNotificationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await subscription.WaitAsync(managedNotificationCancellation.Token);

        watcher.ObservePath(Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Ignored.zip"));
        watcher.ObservePath(Path.Combine(runtimeRoot.ServerRootPath, "logs", "server.log"));

        using var unmanagedNotificationCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await subscription.WaitAsync(unmanagedNotificationCancellation.Token));
    }

    [Fact]
    public async Task WatcherOverflowInvalidatesRuntimeDiffWithoutComputingIt()
    {
        using var runtimeRoot = new TemporaryRuntimeRoot();
        Directory.CreateDirectory(runtimeRoot.ServerRootPath);
        using var notifier = new RuntimeChangeNotifier();
        using var subscription = notifier.Subscribe();
        var watcher = new RuntimeChangeWatcher(
            Options.Create(new RuntimeOptions { ServerRootPath = runtimeRoot.ServerRootPath }),
            new ManagedFileMapping(),
            notifier,
            NullLogger<RuntimeChangeWatcher>.Instance);

        watcher.ObserveWatcherError(new InternalBufferOverflowException());

        using var notificationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await subscription.WaitAsync(notificationCancellation.Token);
    }

    private sealed class TemporaryRuntimeRoot : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N"));

        public string ServerRootPath => Path.Combine(RootPath, "server");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
