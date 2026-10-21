namespace RustServer.Plugins.Features.PluginManagement.Models;

public enum SourceKind
{
    Development,
    Release
}

public sealed record Source
{
    public const string DevelopmentBranchName = "develop";

    private Source(SourceKind kind, Release? release)
    {
        Kind = kind;
        Release = release;
    }

    public static Source Development { get; } = new(SourceKind.Development, null);

    public SourceKind Kind { get; }

    public Release? Release { get; }

    public string BranchName => Kind switch
    {
        SourceKind.Development => DevelopmentBranchName,
        SourceKind.Release => Release!.BranchName,
        _ => throw new InvalidOperationException($"Unsupported source kind: {Kind}.")
    };

    public static Source ForRelease(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return new Source(SourceKind.Release, release);
    }
}
