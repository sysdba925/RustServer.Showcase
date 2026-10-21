namespace RustServer.Versions.Api.Versions;

public interface IOxideVersionProvider
{
    Task<OxideVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken);
}
