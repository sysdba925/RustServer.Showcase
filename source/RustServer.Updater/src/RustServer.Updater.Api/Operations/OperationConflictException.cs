namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Indicates that an application already has an operation that can still perform work.
/// </summary>
public sealed class OperationConflictException(string applicationName)
    : InvalidOperationException($"Application '{applicationName}' already has an active operation.");
