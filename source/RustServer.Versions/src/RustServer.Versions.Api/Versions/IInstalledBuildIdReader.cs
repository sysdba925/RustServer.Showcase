namespace RustServer.Versions.Api.Versions;

public interface IInstalledBuildIdReader
{
    ulong? ReadBuildId(string appManifestPath);
}
