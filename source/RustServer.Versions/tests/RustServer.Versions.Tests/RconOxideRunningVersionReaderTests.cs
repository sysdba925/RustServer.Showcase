using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class RconOxideRunningVersionReaderTests
{
    [Fact]
    public async Task ReadVersionAsync_ExecutesOxideVersionAndParsesItsVersionLine()
    {
        var client = new RecordingRconClient(
            new RconCommandResult("""
                Oxide.Rust Version: 2.0.7726
                Oxide.Rust Branch: develop
                """));
        var reader = CreateReader(client);

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Equal("oxide.version", client.ReceivedCommand);
        Assert.Equal("2.0.7726", version?.ToString());
    }

    [Fact]
    public async Task ReadVersionAsync_ParsesActualOxideWebRconLogResponse()
    {
        var client = new RecordingRconClient(
            new RconCommandResult(
                "[Oxide] 20:11 [Info] Oxide.Rust Version: 2.0.7830\nOxide.Rust Branch: master"));
        var reader = CreateReader(client);

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Equal("oxide.version", client.ReceivedCommand);
        Assert.Equal("2.0.7830", version?.ToString());
    }

    [Theory]
    [InlineData("Oxide.Rust Branch: master")]
    [InlineData("[Oxide] 20:11 [Info] Oxide.Rust Branch: master")]
    [InlineData("Some other output Oxide.Rust Version: 2.0.7830")]
    [InlineData("Oxide.Rust Version: invalid")]
    [InlineData("Oxide.Rust Version: 2.0.7830\nOxide.Rust Version: 2.0.7830")]
    public async Task ReadVersionAsync_ReturnsNullWhenOxideVersionResponseIsMalformed(string response)
    {
        var reader = CreateReader(new RecordingRconClient(new RconCommandResult(response)));

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task ReadVersionAsync_ReturnsNullWhenRconTransportIsUnavailable()
    {
        var reader = CreateReader(new RecordingRconClient(
            new RconClientException("connection failed", new InvalidOperationException())));

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task ReadVersionAsync_DoesNotTreatUnexpectedErrorsAsServerStopped()
    {
        var reader = CreateReader(new RecordingRconClient(
            new InvalidOperationException("unexpected failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));
    }

    private static RconOxideRunningVersionReader CreateReader(IRconClient client) =>
        new(client, NullLogger<RconOxideRunningVersionReader>.Instance);

    private sealed class RecordingRconClient : IRconClient
    {
        private readonly RconCommandResult? _result;
        private readonly Exception? _exception;

        public RecordingRconClient(RconCommandResult result)
        {
            _result = result;
        }

        public RecordingRconClient(Exception exception)
        {
            _exception = exception;
        }

        public string? ReceivedCommand { get; private set; }

        public Task<RconCommandResult> ExecuteCommandAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            ReceivedCommand = command;

            if (_exception is not null)
            {
                return Task.FromException<RconCommandResult>(_exception);
            }

            return Task.FromResult(_result!);
        }
    }
}
