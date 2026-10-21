namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Durable result of a verified release package preparation.
/// The staging path is not an activated release path.
/// </summary>
public sealed record PackagePreparationResult(
    string ApplicationName,
    string ReleaseTag,
    string StagingPath,
    string VerifiedChecksum,
    DateTimeOffset PreparedAt);
