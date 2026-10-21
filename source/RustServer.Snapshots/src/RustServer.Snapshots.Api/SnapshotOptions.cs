public sealed class SnapshotOptions
{
    public const string SectionName = "Snapshots";

    public string ServerIdentityPath { get; init; } = string.Empty;

    public string OxideDataPath { get; init; } = string.Empty;

    public string OxideConfigPath { get; init; } = string.Empty;

    public string OxidePluginsPath { get; init; } = string.Empty;

    public string OxideLangPath { get; init; } = string.Empty;

    public string StoragePath { get; init; } = string.Empty;

    public int RetentionCount { get; init; } = 10;

    public long MinimumFreeSpaceBytes { get; init; } = 536_870_912;
}

public sealed class ControlApiOptions
{
    public const string SectionName = "RustServerControlApi";

    public string BaseUrl { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
