namespace RustServer.Versions.Api.Versions;

public interface IOxideAvailableVersionReader
{
    Task<OxideVersion> ReadVersionAsync(CancellationToken cancellationToken);
}
