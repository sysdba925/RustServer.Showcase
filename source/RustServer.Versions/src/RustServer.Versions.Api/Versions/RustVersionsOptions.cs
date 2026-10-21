namespace RustServer.Versions.Api.Versions;

public sealed class RustVersionsOptions
{
    public const string SectionName = "RustVersions";

    public string AppManifestPath { get; set; } = string.Empty;

    public string Branch { get; set; } = "public";
}
