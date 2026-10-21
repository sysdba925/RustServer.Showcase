using Microsoft.AspNetCore.Diagnostics;
using RustServer.Plugins.Features.PluginManagement.Operations;
using System.Text.Json;

namespace RustServer.Plugins.Infrastructure.Http;

public static class OperationHistoryExceptionHandlingExtensions
{
    public static IServiceCollection AddOperationHistoryExceptionHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<OperationHistoryExceptionHandler>();
        services.AddExceptionHandler(options => options.ExceptionHandler = RethrowUnhandledExceptionAsync);

        return services;
    }

    private static Task RethrowUnhandledExceptionAsync(HttpContext context) =>
        Task.FromException(
            context.Features.Get<IExceptionHandlerFeature>()?.Error ??
            new InvalidOperationException("The unhandled exception is unavailable."));
}

public sealed class OperationHistoryExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not OperationHistoryException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new ApiErrorResponse
            {
                Code = "OperationHistoryUnavailable",
                Message = "Persistent operation history is unavailable."
            },
            cancellationToken: cancellationToken);

        return true;
    }
}
