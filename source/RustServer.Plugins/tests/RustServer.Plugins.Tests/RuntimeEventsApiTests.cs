using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Controllers;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeEventsApiTests
{
    [Fact]
    public async Task StreamsDebouncedRuntimeChangedEventAndReleasesOnCancellation()
    {
        using var notifier = new RuntimeChangeNotifier();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestAborted = cancellation.Token;
        httpContext.Response.Body = responseBody;
        var controller = new RuntimeEventsController(notifier)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var streamingTask = controller.GetAsync(cancellation.Token);
        await WaitUntilAsync(
            () => Encoding.UTF8.GetString(responseBody.ToArray()).Contains(": connected", StringComparison.Ordinal),
            cancellation.Token);
        notifier.NotifyRuntimeFilesMayHaveChanged();

        await WaitUntilAsync(
            () => Encoding.UTF8.GetString(responseBody.ToArray()).Contains("event: runtimeChanged\ndata: {}\n\n", StringComparison.Ordinal),
            cancellation.Token);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);

        cancellation.Cancel();
        await streamingTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
