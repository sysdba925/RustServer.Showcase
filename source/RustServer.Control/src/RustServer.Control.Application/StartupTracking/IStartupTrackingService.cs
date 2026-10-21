using RustServer.Control.Domain.StartupTracking;

namespace RustServer.Control.Application.StartupTracking;

public interface IStartupTrackingService
{
    StartupTrackingSchema GetSchema();

    Task<StartupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
