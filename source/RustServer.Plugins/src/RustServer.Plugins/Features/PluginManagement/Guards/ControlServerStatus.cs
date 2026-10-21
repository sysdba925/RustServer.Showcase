namespace RustServer.Plugins.Features.PluginManagement.Guards;

public sealed class ControlServerStatus(HttpClient httpClient) : IServerStatus
{
    public async Task<ServerStatusSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/server/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServerStatusUnavailableException(
                    "RustServer.Control did not return a successful server status response.");
            }

            var status = await response.Content.ReadFromJsonAsync<ControlServerStatusResponse>(cancellationToken);
            if (status is null)
            {
                throw new ServerStatusUnavailableException(
                    "RustServer.Control returned an empty server status response.");
            }

            return new ServerStatusSnapshot(status.IsRunning, status.IsReady);
        }
        catch (ServerStatusUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new ServerStatusUnavailableException(
                "RustServer.Control server status timed out.",
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            throw new ServerStatusUnavailableException(
                "RustServer.Control server status is unavailable.",
                exception);
        }
    }

    private sealed record ControlServerStatusResponse(bool IsRunning, bool IsReady);
}
