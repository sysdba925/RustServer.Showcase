public interface IControlServerClient
{
    Task<bool> IsServerRunningAsync(CancellationToken cancellationToken);
}

public sealed class ControlServerClient(HttpClient httpClient) : IControlServerClient
{
    public async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/server/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ControlServerUnavailableException();
            }

            var status = await response.Content.ReadFromJsonAsync<ControlServerStatus>(cancellationToken);
            if (status is null)
            {
                throw new ControlServerUnavailableException();
            }

            return status.IsRunning;
        }
        catch (ControlServerUnavailableException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ControlServerUnavailableException(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ControlServerUnavailableException(exception);
        }
        catch (NotSupportedException exception)
        {
            throw new ControlServerUnavailableException(exception);
        }
    }

    private sealed record ControlServerStatus(bool IsRunning);
}

public sealed class ControlServerUnavailableException : Exception
{
    public ControlServerUnavailableException()
        : base("RustServer.Control server status is unavailable.")
    {
    }

    public ControlServerUnavailableException(Exception innerException)
        : base("RustServer.Control server status is unavailable.", innerException)
    {
    }
}
