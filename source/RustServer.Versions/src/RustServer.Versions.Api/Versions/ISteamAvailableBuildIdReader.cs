namespace RustServer.Versions.Api.Versions;

public interface ISteamAvailableBuildIdReader
{
    Task<ulong> ReadBuildIdAsync(string branch, CancellationToken cancellationToken);
}
