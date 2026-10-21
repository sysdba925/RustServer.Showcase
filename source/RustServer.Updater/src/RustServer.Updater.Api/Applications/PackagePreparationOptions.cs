namespace RustServer.Updater.Api.Applications;

/// <summary>Limits and storage location used while preparing release packages.</summary>
public sealed class PackagePreparationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PackagePreparation";

    /// <summary>Persistent state root, owned by the Updater service account.</summary>
    public string StoragePath { get; init; } = "/var/lib/rustserver/updater/staging";

    /// <summary>Largest accepted release archive, in bytes.</summary>
    public long MaximumArchiveBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Largest accepted checksum asset, in bytes.</summary>
    public long MaximumChecksumBytes { get; init; } = 1024 * 1024;

    /// <summary>Largest total size of regular files expanded from one archive, in bytes.</summary>
    public long MaximumExtractedBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Largest number of non-root archive entries accepted from one archive.</summary>
    public int MaximumArchiveEntries { get; init; } = 20_000;
}
