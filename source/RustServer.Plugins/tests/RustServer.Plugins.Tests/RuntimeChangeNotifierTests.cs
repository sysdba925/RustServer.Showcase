using System.Threading.Channels;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeChangeNotifierTests
{
    [Fact]
    public async Task CoalescesMultipleFilesystemInvalidationsForLiveSubscription()
    {
        using var notifier = new RuntimeChangeNotifier();
        using var subscription = notifier.Subscribe();
        using var receivedCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        notifier.NotifyRuntimeFilesMayHaveChanged();
        notifier.NotifyRuntimeFilesMayHaveChanged();
        notifier.NotifyRuntimeFilesMayHaveChanged();

        await subscription.WaitAsync(receivedCancellation.Token);

        using var noSecondEventCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await subscription.WaitAsync(noSecondEventCancellation.Token));
    }

    [Fact]
    public async Task ReleasingSubscriptionStopsDeliveryWithoutLeavingStoredEvents()
    {
        using var notifier = new RuntimeChangeNotifier();
        var subscription = notifier.Subscribe();
        subscription.Dispose();
        notifier.NotifyRuntimeFilesMayHaveChanged();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await subscription.WaitAsync(cancellation.Token));
    }
}
