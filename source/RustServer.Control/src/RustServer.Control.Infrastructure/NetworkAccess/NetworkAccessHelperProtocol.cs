using System.Text.Json.Serialization;

namespace RustServer.Control.Infrastructure.NetworkAccess;

internal sealed record NetworkAccessHelperRequest(
    [property: JsonRequired] bool PublicEnabled,
    [property: JsonRequired] int MaintenancePort);

internal sealed record NetworkAccessHelperResponse(
    [property: JsonRequired] bool Succeeded,
    string? FailureKind,
    string? Detail);
