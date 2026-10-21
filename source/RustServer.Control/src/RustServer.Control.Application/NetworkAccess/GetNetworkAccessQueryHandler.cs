using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Application.NetworkAccess;

/// <summary>
/// Reads the persisted NetworkAccess configuration.
/// </summary>
public sealed class GetNetworkAccessQueryHandler(INetworkAccessSettingsStore settingsStore)
{
    public NetworkAccessSettings Handle() => settingsStore.Get();
}
