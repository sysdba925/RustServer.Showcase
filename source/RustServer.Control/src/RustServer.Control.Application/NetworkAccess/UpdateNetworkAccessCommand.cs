namespace RustServer.Control.Application.NetworkAccess;

public sealed record UpdateNetworkAccessCommand(bool PublicEnabled, int MaintenancePort);
