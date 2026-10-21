using Grpc.Core;

namespace RustServer.Rcon.Grpc;

/// <summary>
/// Exposes the Rust WebRCON contract over gRPC.
/// </summary>
public sealed class RconGrpcService : RconService.RconServiceBase
{
    private const string ConnectionStatusCommand = "status";

    private readonly IServerCommandClient _commandClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RconGrpcService"/> class.
    /// </summary>
    /// <param name="commandClient">The registered Rust WebRCON client.</param>
    public RconGrpcService(IServerCommandClient commandClient)
    {
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
    }

    /// <inheritdoc />
    public override async Task<ExecuteCommandResponse> ExecuteCommand(
        ExecuteCommandRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "The Rust console command must not be empty."));
        }

        try
        {
            var result = await _commandClient.ExecuteAsync(request.Command, context.CancellationToken);
            return new ExecuteCommandResponse { Response = result.ResponseMessage };
        }
        catch (RustServerRconException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public override async Task<GetConnectionStatusResponse> GetConnectionStatus(
        GetConnectionStatusRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _commandClient.ExecuteAsync(ConnectionStatusCommand, context.CancellationToken);
            return new GetConnectionStatusResponse { State = ConnectionState.Connected };
        }
        catch (RustServerRconException exception)
        {
            return new GetConnectionStatusResponse
            {
                State = ConnectionState.Disconnected,
                Detail = exception.Message
            };
        }
    }

    private static RpcException CreateUnavailableException(RustServerRconException exception) => new(
        new Status(
            StatusCode.Unavailable,
            exception.Message));
}
