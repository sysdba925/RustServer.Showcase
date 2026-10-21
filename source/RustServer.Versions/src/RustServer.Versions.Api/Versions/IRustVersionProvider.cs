namespace RustServer.Versions.Api.Versions;

public interface IRustVersionProvider
{
    Task<RustVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken);
}
