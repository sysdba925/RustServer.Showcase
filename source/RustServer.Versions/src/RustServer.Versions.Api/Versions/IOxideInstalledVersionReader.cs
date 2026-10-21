namespace RustServer.Versions.Api.Versions;

public interface IOxideInstalledVersionReader
{
    OxideVersion? ReadVersion(string assemblyPath);
}
