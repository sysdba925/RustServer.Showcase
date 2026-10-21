using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Executor;

/// <summary>Reads the registered deployment target from the Updater runtime registry.</summary>
internal interface IDeploymentApplicationRegistry
{
    /// <summary>Returns one registered application or rejects an unknown request target.</summary>
    ApplicationRegistration GetRequired(string applicationName);
}

/// <summary>Production registry reader. It rereads the runtime file for every privileged action.</summary>
internal sealed class FileDeploymentApplicationRegistry : IDeploymentApplicationRegistry
{
    private readonly string _applicationRegistryPath;

    /// <summary>Creates a reader for the fixed Updater runtime registry.</summary>
    public FileDeploymentApplicationRegistry(string applicationRegistryPath)
    {
        if (string.IsNullOrWhiteSpace(applicationRegistryPath)
            || (!applicationRegistryPath.StartsWith("/", StringComparison.Ordinal)
                && !Path.IsPathFullyQualified(applicationRegistryPath)))
        {
            throw new ArgumentException("Application registry path must be absolute.", nameof(applicationRegistryPath));
        }

        _applicationRegistryPath = Path.GetFullPath(applicationRegistryPath);
    }

    /// <inheritdoc />
    public ApplicationRegistration GetRequired(string applicationName)
    {
        var applications = ApplicationRegistrationIniParser.ParseFile(_applicationRegistryPath);
        return applications.SingleOrDefault(application => string.Equals(application.Name, applicationName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DeploymentExecutionException("Deployment request does not name a registered application.");
    }
}
