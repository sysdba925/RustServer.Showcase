namespace RustServer.Updater.Api.Operations;

/// <summary>Response returned when durable background work has been accepted.</summary>
public sealed record OperationAcceptedResponse(Guid OperationId);
