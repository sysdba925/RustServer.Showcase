namespace RustServer.Updater.Api.Applications;

/// <summary>Represents a non-secret failure while resolving or preparing a release package.</summary>
public sealed class PackagePreparationException(string message) : Exception(message);
