namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Configuration for durable operation records.
/// </summary>
public sealed class OperationStoreOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Operations";

    /// <summary>
    /// Directory containing one JSON record per operation.
    /// </summary>
    public string StoragePath { get; set; } = "/var/lib/rustserver/updater/operations";
}
