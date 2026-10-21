namespace RustServer.Versions.Api.Versions;

public interface IOxideRunningVersionReader
{
    Task<OxideVersion?> ReadVersionAsync(CancellationToken cancellationToken);
}
