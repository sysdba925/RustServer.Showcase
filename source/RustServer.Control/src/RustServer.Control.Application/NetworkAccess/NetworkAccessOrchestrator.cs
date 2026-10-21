using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Application.NetworkAccess;

/// <summary>
/// Serializes rule changes and keeps the persisted value aligned with successfully applied rules.
/// </summary>
public sealed class NetworkAccessOrchestrator(
    INetworkAccessSettingsStore settingsStore,
    INetworkAccessRuleApplier ruleApplier)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<NetworkAccessSettings> ApplyAndPersistAsync(
        NetworkAccessSettings requested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requested);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = settingsStore.Get();
            // Saving desired Public ON never opens the ports before Rust is ready.
            await ruleApplier.ApplyAsync(requested with { PublicEnabled = false }, cancellationToken);

            try
            {
                settingsStore.Save(requested);
            }
            catch
            {
                await TryRestoreRulesAsync(current);
                throw;
            }

            return requested;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReconcileAsync(bool rustReady, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var desired = settingsStore.Get();
            await ruleApplier.ApplyAsync(desired with { PublicEnabled = desired.PublicEnabled && rustReady }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SuspendPublicAccessAsync(CancellationToken cancellationToken = default) =>
        ReconcileAsync(false, cancellationToken);

    private async Task TryRestoreRulesAsync(NetworkAccessSettings current)
    {
        try
        {
            await ruleApplier.ApplyAsync(current with { PublicEnabled = false });
        }
        catch
        {
            // The persistence error remains the actionable error. A future explicit update or
            // startup reconciliation will retry the last persisted settings.
        }
    }
}
