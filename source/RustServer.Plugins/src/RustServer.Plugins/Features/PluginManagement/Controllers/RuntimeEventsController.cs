using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/runtime/events")]
public sealed class RuntimeEventsController(RuntimeChangeNotifier notifier) : ControllerBase
{
    private static readonly byte[] ConnectedComment = ": connected\n\n"u8.ToArray();
    private static readonly byte[] RuntimeChangedEvent = "event: runtimeChanged\ndata: {}\n\n"u8.ToArray();

    [HttpGet]
    [Produces("text/event-stream")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/event-stream")]
    public async Task GetAsync(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = notifier.Subscribe();

        try
        {
            await Response.StartAsync(cancellationToken);
            await Response.Body.WriteAsync(ConnectedComment, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await subscription.WaitAsync(cancellationToken);
                await Response.Body.WriteAsync(RuntimeChangedEvent, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A disconnected SSE client is normal. The using scope releases its subscription.
        }
        catch (IOException) when (Response.HasStarted)
        {
            // The peer may close an already-started SSE response without a cancellation signal.
        }
    }
}
