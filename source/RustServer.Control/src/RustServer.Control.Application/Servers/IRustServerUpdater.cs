namespace RustServer.Control.Application.Servers;

public interface IRustServerUpdater
{
    Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default);
}
