using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Infrastructure.Http;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class OperationHistoryExceptionHandlerTests
{
    [Fact]
    public async Task OperationHistoryException_ReturnsServiceUnavailableApiError()
    {
        await using var application = await StartApplicationAsync(
            new OperationHistoryException("The database is unavailable."));
        using var client = application.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OperationHistoryUnavailable", error.RootElement.GetProperty("code").GetString());
        Assert.Equal("Persistent operation history is unavailable.", error.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnrelatedException_IsNotHandled()
    {
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var handler = new OperationHistoryExceptionHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("Unexpected failure."),
            CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, responseBody.Length);
    }

    private static async Task<WebApplication> StartApplicationAsync(Exception exception)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOperationHistoryExceptionHandling();

        var application = builder.Build();
        application.UseExceptionHandler();
        application.Run(_ => Task.FromException(exception));
        await application.StartAsync();

        return application;
    }
}
