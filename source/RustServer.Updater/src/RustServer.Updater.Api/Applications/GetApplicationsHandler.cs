namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Handles retrieval of every registered application.
/// </summary>
public sealed class GetApplicationsHandler(
    IApplicationRegistry applicationRegistry)
{
    /// <summary>
    /// Retrieves every registered application.
    /// </summary>
    public IReadOnlyCollection<ApplicationResponse> Handle()
    {
        var snapshots = applicationRegistry.GetSnapshots()
            .ToDictionary(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase);

        return applicationRegistry
            .GetAll()
            .Select(application => ToResponse(
                application,
                snapshots[application.Name]))
            .ToArray();
    }

    internal static ApplicationResponse ToResponse(
        ApplicationRegistration application,
        ApplicationSnapshot snapshot) => new(
        application.Name,
        snapshot.InstalledVersion,
        snapshot.AvailableVersion,
        snapshot.Status);
}
