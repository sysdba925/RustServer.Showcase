using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RustServer.Rcon.Tests;

public sealed class WebRconServerCommandClientTests
{
    [Fact]
    public async Task ExecuteAsync_SerializesWebRconCommand()
    {
        var connection = new FakeWebRconConnection();
        connection.EnqueueTextResponse(1001, "ok");
        var client = CreateClient(connection);

        await client.ExecuteAsync("status");

        var payload = Assert.Single(connection.SentPayloads);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(1001, root.GetProperty("Identifier").GetInt32());
        Assert.Equal("status", root.GetProperty("Message").GetString());
        Assert.Equal("WebRcon", root.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyTheResponseWithMatchingIdentifier()
    {
        var connection = new FakeWebRconConnection();
        connection.EnqueueTextResponse(999, "unrelated");
        connection.EnqueueTextResponse(1001, "expected");
        var client = CreateClient(connection);

        var result = await client.ExecuteAsync("status");

        Assert.Equal("expected", result.ResponseMessage);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"Message\":\"missing identifier\"}")]
    [InlineData("{\"Identifier\":1001}")]
    public async Task ExecuteAsync_WhenResponseIsInvalid_ThrowsSanitizedException(string response)
    {
        const string password = "uri-sensitive-password?/#";
        var connection = new FakeWebRconConnection();
        connection.EnqueueText(response);
        var client = CreateClient(connection, password: password);

        var exception = await Assert.ThrowsAsync<RustServerRconException>(() => client.ExecuteAsync("status"));

        Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenResponseExceedsConfiguredLimit_ThrowsSanitizedException()
    {
        var connection = new FakeWebRconConnection();
        connection.EnqueueFrame(new byte[10], WebSocketMessageType.Text, endOfMessage: false);
        var client = CreateClient(connection, maxResponseBytes: 10);

        var exception = await Assert.ThrowsAsync<RustServerRconException>(() => client.ExecuteAsync("status"));

        Assert.Contains("size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWebSocketClosesBeforeResponse_ThrowsSanitizedException()
    {
        var connection = new FakeWebRconConnection();
        connection.EnqueueFrame([], WebSocketMessageType.Close, endOfMessage: true);
        var client = CreateClient(connection);

        var exception = await Assert.ThrowsAsync<RustServerRconException>(() => client.ExecuteAsync("sv quit"));

        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandResponseTimesOut_ThrowsSanitizedException()
    {
        var connection = new FakeWebRconConnection { WaitForReceiveCancellation = true };
        var client = CreateClient(connection, commandTimeout: TimeSpan.FromMilliseconds(10));

        var exception = await Assert.ThrowsAsync<RustServerRconException>(() => client.ExecuteAsync("status"));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var connection = new FakeWebRconConnection { WaitForConnectCancellation = true };
        var client = CreateClient(connection);
        using var cancellationTokenSource = new CancellationTokenSource();

        var execution = client.ExecuteAsync("status", cancellationTokenSource.Token);
        await connection.ConnectStarted.Task;
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConnectionFails_DoesNotExposePassword()
    {
        const string password = "password/with?reserved#characters";
        var connection = new FakeWebRconConnection
        {
            ConnectException = new WebSocketException($"Connection failed for {password}")
        };
        var client = CreateClient(connection, password: password);

        var exception = await Assert.ThrowsAsync<RustServerRconException>(() => client.ExecuteAsync("status"));

        Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ws://", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_EscapesUriSensitivePasswordCharacters()
    {
        const string password = "pa ss/@?%#word";
        var connection = new FakeWebRconConnection();
        connection.EnqueueTextResponse(1001, "ok");
        var client = CreateClient(connection, password: password);

        await client.ExecuteAsync("status");

        var connectionUri = Assert.IsType<Uri>(connection.ConnectionUri);
        Assert.Equal(
            Uri.EscapeDataString(password),
            connectionUri.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
    }

    private static WebRconServerCommandClient CreateClient(
        FakeWebRconConnection connection,
        string password = "test-password",
        int maxResponseBytes = 65_536,
        TimeSpan? commandTimeout = null) =>
        new(
            new RustServerRconOptions
            {
                Host = "127.0.0.1",
                Port = 28016,
                Password = password,
                ConnectTimeout = TimeSpan.FromSeconds(1),
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(1),
                MaxResponseBytes = maxResponseBytes
            },
            new FakeWebRconConnectionFactory(connection));

    private sealed class FakeWebRconConnectionFactory : IWebRconConnectionFactory
    {
        private readonly FakeWebRconConnection _connection;

        public FakeWebRconConnectionFactory(FakeWebRconConnection connection)
        {
            _connection = connection;
        }

        public IWebRconConnection Create() => _connection;
    }

    private sealed class FakeWebRconConnection : IWebRconConnection
    {
        private readonly Queue<ReceiveFrame> _receiveFrames = [];

        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri? ConnectionUri { get; private set; }

        public List<byte[]> SentPayloads { get; } = [];

        public Exception? ConnectException { get; init; }

        public bool WaitForConnectCancellation { get; init; }

        public bool WaitForReceiveCancellation { get; init; }

        public async Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken)
        {
            ConnectionUri = connectionUri;
            ConnectStarted.SetResult();

            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            if (WaitForConnectCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public Task SendAsync(
            ArraySegment<byte> payload,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentPayloads.Add(payload.ToArray());
            return Task.CompletedTask;
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (WaitForReceiveCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var frame = _receiveFrames.Count > 0
                ? _receiveFrames.Dequeue()
                : new ReceiveFrame([], WebSocketMessageType.Close, EndOfMessage: true);

            Assert.True(frame.Payload.Length <= buffer.Count);
            frame.Payload.CopyTo(buffer.Array!, buffer.Offset);

            return new WebSocketReceiveResult(
                frame.Payload.Length,
                frame.MessageType,
                frame.EndOfMessage);
        }

        public void EnqueueTextResponse(int identifier, string message) =>
            EnqueueText(JsonSerializer.Serialize(new { Identifier = identifier, Message = message }));

        public void EnqueueText(string message) =>
            EnqueueFrame(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true);

        public void EnqueueFrame(byte[] payload, WebSocketMessageType messageType, bool endOfMessage) =>
            _receiveFrames.Enqueue(new ReceiveFrame(payload, messageType, endOfMessage));

        public void Dispose()
        {
        }

        private sealed record ReceiveFrame(
            byte[] Payload,
            WebSocketMessageType MessageType,
            bool EndOfMessage);
    }
}
