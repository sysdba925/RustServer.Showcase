namespace RustServer.Control.Application.StartupTracking;

public interface IStartupLogSource
{
    Task<StartupRunLog?> ReadCurrentRunAsync(CancellationToken cancellationToken = default);
}
