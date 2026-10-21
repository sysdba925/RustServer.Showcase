using System.Net.WebSockets;

namespace RustServer.Rcon;

internal interface IWebRconConnection : IDisposable
{
    Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken);

    Task SendAsync(
        ArraySegment<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken);
}

internal interface IWebRconConnectionFactory
{
    IWebRconConnection Create();
}

internal sealed class ClientWebRconConnectionFactory : IWebRconConnectionFactory
{
    public IWebRconConnection Create() => new ClientWebRconConnection();
}

internal sealed class ClientWebRconConnection : IWebRconConnection
{
    private readonly ClientWebSocket _webSocket = new();

    public Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken) =>
        _webSocket.ConnectAsync(connectionUri, cancellationToken);

    public Task SendAsync(
        ArraySegment<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        _webSocket.SendAsync(payload, messageType, endOfMessage, cancellationToken);

    public Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken) =>
        _webSocket.ReceiveAsync(buffer, cancellationToken);

    public void Dispose() => _webSocket.Dispose();
}
