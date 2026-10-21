using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Application.NetworkAccess;

/// <summary>
/// Durable, Control-owned storage for the requested NetworkAccess settings.
/// </summary>
public interface INetworkAccessSettingsStore
{
    NetworkAccessSettings Get();

    void Save(NetworkAccessSettings settings);
}
