namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Handles retrieval of one operation for the API.
/// </summary>
public sealed class GetOperationHandler(IOperationManager operationManager)
{
    /// <summary>
    /// Retrieves an operation by identifier.
    /// </summary>
    public async Task<OperationResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var operation = await operationManager.GetAsync(id, cancellationToken);
        return operation is null ? null : OperationResponse.From(operation);
    }
}
