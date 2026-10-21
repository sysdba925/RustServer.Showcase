using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Application.NetworkAccess;

/// <summary>
/// Validates a NetworkAccess command before it reaches the privileged rule applier.
/// </summary>
public sealed class UpdateNetworkAccessCommandHandler(NetworkAccessOrchestrator orchestrator)
{
    public Task<NetworkAccessSettings> HandleAsync(
        UpdateNetworkAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ValidateMaintenancePort(command.MaintenancePort);
        return orchestrator.ApplyAndPersistAsync(
            new NetworkAccessSettings(command.PublicEnabled, command.MaintenancePort),
            cancellationToken);
    }

    private static void ValidateMaintenancePort(int port)
    {
        if (port is < 1 or > 65_535)
        {
            throw new NetworkAccessValidationException(
                "maintenancePort",
                "MaintenancePort must be between 1 and 65535.");
        }

        if (port is NetworkAccessSettings.GamePort or NetworkAccessSettings.QueryPort)
        {
            throw new NetworkAccessValidationException(
                "maintenancePort",
                "MaintenancePort must differ from the public game and Steam Query ports.");
        }
    }
}
