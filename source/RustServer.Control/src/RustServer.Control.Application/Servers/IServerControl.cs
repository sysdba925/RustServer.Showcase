using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Application.Servers;

public interface IServerControl
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default);

    Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default);

    Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default);
}
