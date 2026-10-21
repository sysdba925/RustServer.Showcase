namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Handles bounded retrieval of operation history for the API.
/// </summary>
public sealed class GetOperationsHandler(IOperationManager operationManager)
{
    /// <summary>
    /// Default number of operation records returned by the collection endpoint.
    /// </summary>
    public const int DefaultLimit = 50;

    /// <summary>
    /// Maximum number of operation records returned by the collection endpoint.
    /// </summary>
    public const int MaximumLimit = 100;

    /// <summary>
    /// Retrieves the newest operation records up to the supplied limit.
    /// </summary>
    public async Task<IReadOnlyCollection<OperationResponse>> HandleAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, $"Limit must be from 1 to {MaximumLimit}.");
        }

        var operations = await operationManager.GetRecentAsync(limit, cancellationToken);
        return operations.Select(OperationResponse.From).ToArray();
    }
}
