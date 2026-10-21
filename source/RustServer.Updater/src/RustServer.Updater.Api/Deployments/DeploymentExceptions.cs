namespace RustServer.Updater.Api.Deployments;

/// <summary>Raised when no intact Stage 6 package may be activated.</summary>
public sealed class PreparedPackageNotFoundException(string message) : Exception(message);

/// <summary>Raised for an executor failure after its durable result has been written.</summary>
public sealed class DeploymentActivationException(string message) : Exception(message);
