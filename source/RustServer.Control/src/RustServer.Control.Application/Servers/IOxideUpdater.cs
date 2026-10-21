namespace RustServer.Control.Application.Servers;

public interface IOxideUpdater
{
    Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default);
}
