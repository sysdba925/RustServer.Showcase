namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;

public static class CatalogHttpResults
{
    public static IResult From<T>(CatalogResult<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value!)
            : Problem(result.Failure!);
    }

    public static IResult Problem(CatalogFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var statusCode = failure.Kind switch
        {
            CatalogFailureKind.Validation => StatusCodes.Status400BadRequest,
            CatalogFailureKind.NotFound => StatusCodes.Status404NotFound,
            CatalogFailureKind.Conflict => StatusCodes.Status409Conflict,
            CatalogFailureKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            CatalogFailureKind.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
            CatalogFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => throw new InvalidOperationException($"Unsupported catalog failure kind: {failure.Kind}.")
        };

        return Results.Problem(
            statusCode: statusCode,
            title: failure.Title,
            detail: failure.Detail,
            extensions: new Dictionary<string, object?> { ["code"] = failure.Code });
    }
}
