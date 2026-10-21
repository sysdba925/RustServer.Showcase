using RustServer.Plugins.Features.PluginManagement.Guards;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class ExclusiveMutationGuardTests
{
    [Fact]
    public async Task RejectsConcurrentAcquireAndAllowsAcquireAfterLeaseRelease()
    {
        var guard = new ExclusiveMutationGuard();

        var firstLease = guard.TryAcquire();
        Assert.NotNull(firstLease);
        Assert.Null(guard.TryAcquire());

        await firstLease.DisposeAsync();

        var nextLease = guard.TryAcquire();
        Assert.NotNull(nextLease);
        await nextLease.DisposeAsync();
    }
}
