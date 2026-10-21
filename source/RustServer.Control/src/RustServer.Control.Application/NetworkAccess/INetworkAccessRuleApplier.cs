using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Application.NetworkAccess;

/// <summary>
/// Privileged boundary that applies the nftables and Docker-aware rules for a settings value.
/// </summary>
public interface INetworkAccessRuleApplier
{
    Task ApplyAsync(NetworkAccessSettings settings, CancellationToken cancellationToken = default);
}
