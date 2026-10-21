using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Thread-safe non-durable configuration store useful for isolated hosts and tests.
/// Production uses <see cref="FileServerConfigurationStore"/>.
/// </summary>
public sealed class InMemoryServerConfigurationStore : IServerConfigurationStore
{
    private readonly object _sync = new();
    private ServerConfigurationState _state;

    public InMemoryServerConfigurationStore(ServerConfiguration desired, ServerConfiguration? effective = null)
    {
        ArgumentNullException.ThrowIfNull(desired);
        _state = new ServerConfigurationState(desired, effective);
    }

    public ServerConfigurationState Get()
    {
        lock (_sync)
        {
            return _state;
        }
    }

    public void SetDesired(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_sync)
        {
            _state = _state with { Desired = configuration };
        }
    }

    public void RecordEffective(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_sync)
        {
            _state = _state with { Effective = configuration };
        }
    }
}
