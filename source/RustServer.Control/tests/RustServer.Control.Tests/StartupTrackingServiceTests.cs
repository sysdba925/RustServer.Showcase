using RustServer.Control.Application.StartupTracking;
using RustServer.Control.Domain.StartupTracking;
using RustServer.Control.Infrastructure.StartupTracking;

namespace RustServer.Control.Tests;

public sealed class StartupTrackingServiceTests
{
    [Fact]
    public void GetSchema_DoesNotReadDockerLogs()
    {
        var service = new StartupTrackingService(new ThrowingStartupLogSource());

        var schema = service.GetSchema();

        Assert.Same(StartupTrackingSchema.Version1, schema);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenServerIsStopped_ReturnsPendingPlaceholderSnapshot()
    {
        var service = new StartupTrackingService(new FixedStartupLogSource(null));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Null(snapshot.SessionId);
        Assert.False(snapshot.Started);
        Assert.Equal(Enumerable.Repeat(StartupMilestoneState.Pending, 9), snapshot.Milestones);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReconstructsCompletedRunForFreshServiceInstance()
    {
        var run = new StartupRunLog(
            "full-container-id",
            "2026-09-29T17:00:00.123456789Z",
            CompletedRunLines);

        var firstSnapshot = await new StartupTrackingService(new FixedStartupLogSource(run)).GetSnapshotAsync();
        var reconstructedSnapshot = await new StartupTrackingService(new FixedStartupLogSource(run)).GetSnapshotAsync();

        Assert.Equal(firstSnapshot.SessionId, reconstructedSnapshot.SessionId);
        Assert.True(reconstructedSnapshot.Started);
        Assert.Equal(Enumerable.Repeat(StartupMilestoneState.Reached, 9), reconstructedSnapshot.Milestones);
    }

    [Fact]
    public async Task GetSnapshotAsync_NewRunWithSameContainerIdDoesNotInheritPreviousMilestones()
    {
        var source = new MutableStartupLogSource(
            new StartupRunLog(
                "reused-container-id",
                "2026-09-29T17:00:00.000000000Z",
                CompletedRunLines));
        var service = new StartupTrackingService(source);

        var completedRun = await service.GetSnapshotAsync();
        source.Current = new StartupRunLog(
            "reused-container-id",
            "2026-09-29T18:00:00.000000000Z",
            ["Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)"]);
        var newRun = await service.GetSnapshotAsync();

        Assert.NotEqual(completedRun.SessionId, newRun.SessionId);
        Assert.False(newRun.Started);
        Assert.Equal(StartupMilestoneState.Reached, newRun.Milestones[0]);
        Assert.All(newRun.Milestones.Skip(1), state => Assert.Equal(StartupMilestoneState.Pending, state));
    }

    [Fact]
    public async Task GetSnapshotAsync_UsesDifferentSessionIdForDifferentContainerRuns()
    {
        var first = await new StartupTrackingService(new FixedStartupLogSource(
            new StartupRunLog("container-a", "2026-09-29T17:00:00Z", []))).GetSnapshotAsync();
        var second = await new StartupTrackingService(new FixedStartupLogSource(
            new StartupRunLog("container-b", "2026-09-29T17:00:00Z", []))).GetSnapshotAsync();

        Assert.NotNull(first.SessionId);
        Assert.StartsWith("stp_", first.SessionId, StringComparison.Ordinal);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    private static readonly string[] CompletedRunLines =
    [
        "Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)",
        "Begin MonoManager ReloadAssembly",
        "Bootstrap Startup",
        "Loading Scene",
        "Loading Oxide Core v2.0.7143...",
        "SteamServer Initialized",
        "Server startup complete",
        "[Bootstrap] completed in 17.40s",
        "SteamServer Connected"
    ];

    private sealed class FixedStartupLogSource(StartupRunLog? run) : IStartupLogSource
    {
        public Task<StartupRunLog?> ReadCurrentRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(run);
    }

    private sealed class MutableStartupLogSource(StartupRunLog? current) : IStartupLogSource
    {
        public StartupRunLog? Current { get; set; } = current;

        public Task<StartupRunLog?> ReadCurrentRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);
    }

    private sealed class ThrowingStartupLogSource : IStartupLogSource
    {
        public Task<StartupRunLog?> ReadCurrentRunAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Schema retrieval must not read Docker logs.");
    }
}
