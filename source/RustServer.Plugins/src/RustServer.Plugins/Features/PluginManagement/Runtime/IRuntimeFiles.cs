namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public interface IRuntimeFiles
{
    Task<RuntimeAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedFile>> ListAsync(CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(ManagedFile file, CancellationToken cancellationToken);

    Task WriteAsync(ManagedFile file, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task DeleteAsync(ManagedFile file, CancellationToken cancellationToken);
}
