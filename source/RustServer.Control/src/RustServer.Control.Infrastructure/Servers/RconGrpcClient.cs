using Grpc.Core;
using RustServer.Control.Application.Servers;
using RustServer.Rcon.Grpc;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Uses the standalone RCON gRPC service for Rust console operations.
/// </summary>
public sealed class RconGrpcClient : IRconClient
{
    private readonly RconService.RconServiceClient _client;

    public RconGrpcClient(RconService.RconServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<RconCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.ExecuteCommandAsync(
                new ExecuteCommandRequest { Command = command },
                cancellationToken: cancellationToken).ResponseAsync;

            return new RconCommandResult(response.Response);
        }
        catch (RpcException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The RCON command request was canceled.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            throw new RconClientException("The RCON service command request failed.", exception);
        }
    }

    public async Task<RconConnectionState> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetConnectionStatusAsync(
                new GetConnectionStatusRequest(),
                cancellationToken: cancellationToken).ResponseAsync;

            return response.State == ConnectionState.Connected
                ? RconConnectionState.Connected
                : RconConnectionState.Disconnected;
        }
        catch (RpcException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The RCON connection status request was canceled.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            throw new RconClientException("The RCON service connection status request failed.", exception);
        }
    }
}
