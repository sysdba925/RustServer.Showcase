using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Application.Servers;

/// <summary>
/// Owns the desired configuration and the configuration applied to the active Rust process.
/// </summary>
public interface IServerConfigurationStore
{
    ServerConfigurationState Get();

    void SetDesired(ServerConfiguration configuration);

    void RecordEffective(ServerConfiguration configuration);
}
