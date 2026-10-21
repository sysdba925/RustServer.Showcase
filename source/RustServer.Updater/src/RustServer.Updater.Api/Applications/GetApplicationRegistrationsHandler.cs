namespace RustServer.Updater.Api.Applications;

/// <summary>Reads runtime registration definitions without exposing runtime snapshots.</summary>
public sealed class GetApplicationRegistrationsHandler(IApplicationRegistry applicationRegistry)
{
    /// <summary>Gets the resolved read-only registrations.</summary>
    public IReadOnlyCollection<ApplicationRegistration> Handle() => applicationRegistry.GetAll();
}
