using Grpc.Core;
using RustServer.Rcon.Grpc;

namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Uses the standalone RCON gRPC service for Rust console commands.
/// </summary>
public sealed class RconGrpcClient(RconService.RconServiceClient client) : IRconClient
{
    private readonly RconService.RconServiceClient _client =
        client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
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
}
