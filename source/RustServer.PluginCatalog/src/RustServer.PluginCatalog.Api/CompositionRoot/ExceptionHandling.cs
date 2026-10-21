using Microsoft.AspNetCore.Diagnostics;

namespace RustServer.PluginCatalog.Api.CompositionRoot;

public static class ExceptionHandling
{
    public static WebApplication UseApiExceptionHandling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler(exceptionApp =>
            exceptionApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

                if (exception is not null)
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("PluginCatalog.Exceptions");

                    logger.LogError(exception,
                        "Unhandled API exception at {Path}",
                        context.Request.Path);
                }

                var badRequestException = exception as BadHttpRequestException;
                var isPayloadTooLarge = badRequestException?.StatusCode == StatusCodes.Status413PayloadTooLarge;
                var isBadRequest = badRequestException is not null;

                await Results.Problem(
                    statusCode: isPayloadTooLarge
                        ? StatusCodes.Status413PayloadTooLarge
                        : isBadRequest
                            ? StatusCodes.Status400BadRequest
                            : StatusCodes.Status500InternalServerError,
                    title: isPayloadTooLarge
                        ? "The upload is too large."
                        : isBadRequest
                            ? "The request is invalid."
                            : "An unexpected error occurred.",
                    detail: isPayloadTooLarge
                        ? "The request exceeds the configured upload size limit."
                        : isBadRequest
                            ? "The request body or parameters could not be read."
                            : "The server could not complete the request.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = isPayloadTooLarge
                            ? "upload_too_large"
                            : isBadRequest
                                ? "invalid_json"
                                : "internal_error"
                    }).ExecuteAsync(context);
            }));

        return app;
    }
}
