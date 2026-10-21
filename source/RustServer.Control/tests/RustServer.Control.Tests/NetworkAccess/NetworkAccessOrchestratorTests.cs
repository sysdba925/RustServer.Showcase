using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Tests.NetworkAccess;

public sealed class NetworkAccessOrchestratorTests
{
    [Fact]
    public async Task SaveDesiredPublicOn_AppliesGuardFirst_ThenPersists()
    {
        var store = new MemoryStore(NetworkAccessSettings.Default);
        var applier = new RecordingApplier();
        var orchestrator = new NetworkAccessOrchestrator(store, applier);
        var desired = new NetworkAccessSettings(true, 50_016);

        await orchestrator.ApplyAndPersistAsync(desired);

        Assert.Equal(desired, store.Get());
        Assert.Equal([new NetworkAccessSettings(false, 50_016)], applier.Calls);
    }

    [Fact]
    public async Task Reconcile_DoesNotOpenPublicPortsWhenRustIsNotReady()
    {
        var store = new MemoryStore(new NetworkAccessSettings(true, 50_015));
        var applier = new RecordingApplier();
        var orchestrator = new NetworkAccessOrchestrator(store, applier);

        await orchestrator.ReconcileAsync(rustReady: false);
        await orchestrator.ReconcileAsync(rustReady: true);
        await orchestrator.SuspendPublicAccessAsync();

        Assert.Equal(
            [
                new NetworkAccessSettings(false, 50_015),
                new NetworkAccessSettings(true, 50_015),
                new NetworkAccessSettings(false, 50_015)
            ],
            applier.Calls);
    }

    [Fact]
    public async Task FailedGuardApplication_DoesNotChangeDesiredMode()
    {
        var previous = new NetworkAccessSettings(true, 50_015);
        var store = new MemoryStore(previous);
        var applier = new RecordingApplier { Fail = true };
        var orchestrator = new NetworkAccessOrchestrator(store, applier);

        await Assert.ThrowsAsync<NetworkAccessRuleApplicationException>(() =>
            orchestrator.ApplyAndPersistAsync(new NetworkAccessSettings(false, 50_016)));

        Assert.Equal(previous, store.Get());
    }

    private sealed class MemoryStore(NetworkAccessSettings initial) : INetworkAccessSettingsStore
    {
        private NetworkAccessSettings _value = initial;
        public NetworkAccessSettings Get() => _value;
        public void Save(NetworkAccessSettings settings) => _value = settings;
    }

    private sealed class RecordingApplier : INetworkAccessRuleApplier
    {
        public List<NetworkAccessSettings> Calls { get; } = [];
        public bool Fail { get; set; }
        public Task ApplyAsync(NetworkAccessSettings settings, CancellationToken cancellationToken = default)
        {
            Calls.Add(settings);
            if (Fail)
            {
                throw new NetworkAccessRuleApplicationException(
                    NetworkAccessRuleFailureKind.ApplyFailed, "guard application failed");
            }
            return Task.CompletedTask;
        }
    }
}
