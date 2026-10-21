namespace RustServer.Control.Application.NetworkAccess;

public sealed class NetworkAccessSettingsStoreException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class NetworkAccessValidationException(string field, string message)
    : Exception(message)
{
    public string Field { get; } = field;
}

public enum NetworkAccessRuleFailureKind
{
    HelperUnavailable,
    PortInUse,
    ContainerUnavailable,
    UnsupportedDockerFirewall,
    ApplyFailed
}

public sealed class NetworkAccessRuleApplicationException(
    NetworkAccessRuleFailureKind failureKind,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public NetworkAccessRuleFailureKind FailureKind { get; } = failureKind;
}
