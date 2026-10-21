namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Handles retrieval of one registered application.
/// </summary>
public sealed class GetApplicationHandler(
    IApplicationRegistry applicationRegistry)
{
    /// <summary>
    /// Retrieves an application by name.
    /// </summary>
    /// <param name="name">The registered application name.</param>
    /// <returns>The application response when the name is registered; otherwise, <see langword="null"/>.</returns>
    public ApplicationResponse? Handle(string name)
    {
        var application = applicationRegistry.GetByName(name);
        return application is null
            ? null
            : GetApplicationsHandler.ToResponse(
                application,
                applicationRegistry.GetSnapshotByName(application.Name)!);
    }
}
