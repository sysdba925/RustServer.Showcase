using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class RconRustRunningVersionReaderTests
{
    private const string ValidResponse = """
        Protocol: 2633.288.1
        Build Date: 09/22/2026 07:48:10
        Unity Version: 6000.3.15x1-11
        Changeset: 165217
        Branch: release
        """;

    [Fact]
    public async Task ReadVersionAsync_ExecutesVersionAndParsesTheRunningVersion()
    {
        var client = new RecordingRconClient(new RconCommandResult(ValidResponse));
        var reader = CreateReader(client);

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Equal("version", client.ReceivedCommand);
        Assert.NotNull(version);
        Assert.Equal("2633.288.1", version.Protocol);
        Assert.Equal(new DateTime(2026, 9, 22, 7, 48, 10), version.BuildDate);
        Assert.Equal("6000.3.15x1-11", version.UnityVersion);
        Assert.Equal(165217, version.Changeset);
        Assert.Equal("release", version.Branch);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenARequiredLineIsMissing()
    {
        var response = ValidResponse.Replace("Branch: release", string.Empty, StringComparison.Ordinal);
        var reader = CreateReader(new RecordingRconClient(new RconCommandResult(response)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("Branch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenARequiredLineIsDuplicated()
    {
        var response = $"{ValidResponse}\nProtocol: 2633.288.2";
        var reader = CreateReader(new RecordingRconClient(new RconCommandResult(response)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("multiple Protocol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenChangesetIsInvalid()
    {
        var response = ValidResponse.Replace("Changeset: 165217", "Changeset: invalid", StringComparison.Ordinal);
        var reader = CreateReader(new RecordingRconClient(new RconCommandResult(response)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("Changeset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenBuildDateIsInvalid()
    {
        var response = ValidResponse.Replace("Build Date: 09/22/2026 07:48:10", "Build Date: invalid", StringComparison.Ordinal);
        var reader = CreateReader(new RecordingRconClient(new RconCommandResult(response)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("Build Date", exception.Message, StringComparison.Ordinal);
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

    private static RconRustRunningVersionReader CreateReader(IRconClient client) =>
        new(client, NullLogger<RconRustRunningVersionReader>.Instance);

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
