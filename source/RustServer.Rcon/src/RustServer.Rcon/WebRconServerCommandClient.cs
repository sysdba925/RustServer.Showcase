using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;

namespace RustServer.Rcon;

/// <summary>
/// Sends one command through Rust WebRCON per WebSocket connection.
/// </summary>
public sealed class WebRconServerCommandClient : IServerCommandClient
{
    private const string ClientName = "WebRcon";
    private const int FirstCommandIdentifier = 1000;
    private const int ReceiveBufferSize = 4_096;

    private readonly RustServerRconOptions _options;
    private readonly IWebRconConnectionFactory _connectionFactory;
    private int _lastCommandIdentifier = FirstCommandIdentifier;

    public WebRconServerCommandClient(RustServerRconOptions options)
        : this(options, new ClientWebRconConnectionFactory())
    {
    }

    internal WebRconServerCommandClient(
        RustServerRconOptions options,
        IWebRconConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _options = options;
        _connectionFactory = connectionFactory;
    }

    public async Task<ServerCommandResult> ExecuteAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        cancellationToken.ThrowIfCancellationRequested();
        RustServerRconOptionsValidator.Validate(_options);

        var commandIdentifier = NextCommandIdentifier();
        using var webSocket = _connectionFactory.Create();

        try
        {
            await ExecuteWithTimeoutAsync(
                operationCancellationToken => webSocket.ConnectAsync(
                    CreateConnectionUri(),
                    operationCancellationToken),
                _options.ConnectTimeout,
                cancellationToken,
                "connection");

            return await ExecuteWithTimeoutAsync(
                async operationCancellationToken =>
                {
                    var payload = SerializeCommand(commandIdentifier, command);
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        operationCancellationToken);

                    return await ReceiveMatchingResponseAsync(
                        webSocket,
                        commandIdentifier,
                        operationCancellationToken);
                },
                _options.CommandTimeout,
                cancellationToken,
                "command response");
        }
        catch (RustServerRconException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is WebSocketException
                                          or HttpRequestException
                                          or InvalidOperationException
                                          or UriFormatException)
        {
            throw new RustServerRconException("Rust server WebRCON communication failed.");
        }
    }

    private async Task<ServerCommandResult> ReceiveMatchingResponseAsync(
        IWebRconConnection webSocket,
        int commandIdentifier,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var payload = await ReceiveTextMessageAsync(webSocket, cancellationToken);
            var response = ParseResponse(payload);

            if (response.Identifier == commandIdentifier)
            {
                return new ServerCommandResult(response.Message);
            }
        }
    }

    private async Task<byte[]> ReceiveTextMessageAsync(
        IWebRconConnection webSocket,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            using var message = new MemoryStream(Math.Min(_options.MaxResponseBytes, ReceiveBufferSize));
            var receivedBytes = 0;

            while (true)
            {
                var remainingBytes = _options.MaxResponseBytes - receivedBytes;
                if (remainingBytes == 0)
                {
                    throw new RustServerRconException(
                        "Rust server WebRCON response exceeded the configured size limit.");
                }

                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, 0, Math.Min(buffer.Length, remainingBytes)),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new RustServerRconException(
                        "Rust server WebRCON connection closed before a command response was received.");
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new RustServerRconException(
                        "Rust server WebRCON returned an unsupported response type.");
                }

                message.Write(buffer, 0, result.Count);
                receivedBytes += result.Count;

                if (result.EndOfMessage)
                {
                    return message.ToArray();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static WebRconResponse ParseResponse(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Identifier", out var identifierElement)
                || !identifierElement.TryGetInt32(out var identifier)
                || !root.TryGetProperty("Message", out var messageElement)
                || messageElement.ValueKind != JsonValueKind.String)
            {
                throw new RustServerRconException(
                    "Rust server WebRCON returned an invalid command response.");
            }

            return new WebRconResponse(identifier, messageElement.GetString()!);
        }
        catch (JsonException)
        {
            throw new RustServerRconException(
                "Rust server WebRCON returned malformed JSON.");
        }
    }

    private static byte[] SerializeCommand(int commandIdentifier, string command) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new WebRconCommand(commandIdentifier, command, ClientName));

    private Uri CreateConnectionUri()
    {
        try
        {
            return new UriBuilder(Uri.UriSchemeWs, _options.Host, _options.Port)
            {
                Path = Uri.EscapeDataString(_options.Password)
            }.Uri;
        }
        catch (UriFormatException)
        {
            throw new RustServerRconException("Rust server WebRCON connection configuration is invalid.");
        }
    }

    private int NextCommandIdentifier()
    {
        var commandIdentifier = Interlocked.Increment(ref _lastCommandIdentifier);
        if (commandIdentifier > 0)
        {
            return commandIdentifier;
        }

        Interlocked.CompareExchange(
            ref _lastCommandIdentifier,
            FirstCommandIdentifier,
            commandIdentifier);

        return NextCommandIdentifier();
    }

    private static async Task ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string operationName)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
        using var operationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationTokenSource.Token);

        try
        {
            await operation(operationCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && timeoutCancellationTokenSource.IsCancellationRequested)
        {
            throw new RustServerRconException($"Rust server WebRCON {operationName} timed out.");
        }
    }

    private static async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string operationName)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
        using var operationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationTokenSource.Token);

        try
        {
            return await operation(operationCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && timeoutCancellationTokenSource.IsCancellationRequested)
        {
            throw new RustServerRconException($"Rust server WebRCON {operationName} timed out.");
        }
    }

    private sealed record WebRconCommand(int Identifier, string Message, string Name);

    private sealed record WebRconResponse(int Identifier, string Message);
}
