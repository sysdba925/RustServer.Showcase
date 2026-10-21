using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RustServer.Control.Application.NetworkAccess;

namespace RustServer.Control.Infrastructure.NetworkAccess;

/// <summary>
/// Root-only Unix-socket transport for the NetworkAccess configurator. The public API never
/// executes this host; systemd starts it separately with a socket accessible only to the Control
/// group.
/// </summary>
public sealed class NetworkAccessHelperHost
{
    private const int MaximumRequestLength = 1_024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _socketPath;
    private readonly NftablesNetworkConfigurator _configurator;
    private readonly ILogger<NetworkAccessHelperHost> _logger;

    public NetworkAccessHelperHost(
        string socketPath,
        NftablesNetworkConfigurator configurator,
        ILogger<NetworkAccessHelperHost> logger)
    {
        if (string.IsNullOrWhiteSpace(socketPath) || !socketPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("The NetworkAccess helper socket path must be an absolute Unix path.", nameof(socketPath));
        }

        _socketPath = socketPath;
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        EnsureSocketDirectory();
        RemoveStaleSocket();

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(backlog: 16);
        _logger.LogInformation("NetworkAccess helper is listening on its Unix socket.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket client;
                try
                {
                    client = await listener.AcceptAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                using (client)
                {
                    await HandleClientAsync(client, cancellationToken);
                }
            }
        }
        finally
        {
            RemoveStaleSocket();
        }
    }

    private async Task HandleClientAsync(Socket client, CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(client, ownsSocket: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1_024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1_024, leaveOpen: true);

        NetworkAccessHelperResponse response;
        try
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumRequestLength)
            {
                response = Failure(NetworkAccessRuleFailureKind.ApplyFailed, "The NetworkAccess helper request is invalid.");
            }
            else
            {
                var request = JsonSerializer.Deserialize<NetworkAccessHelperRequest>(line, SerializerOptions)
                    ?? throw new JsonException("The NetworkAccess helper request is empty.");
                response = await _configurator.ApplyAsync(request, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            _logger.LogWarning(exception, "NetworkAccess helper received an invalid request.");
            response = Failure(NetworkAccessRuleFailureKind.ApplyFailed, "The NetworkAccess helper request is invalid.");
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions));
        await writer.FlushAsync(cancellationToken);
    }

    private void EnsureSocketDirectory()
    {
        var directory = Path.GetDirectoryName(_socketPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The NetworkAccess helper socket directory does not exist.");
        }
    }

    private void RemoveStaleSocket()
    {
        if (File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
    }

    private static NetworkAccessHelperResponse Failure(NetworkAccessRuleFailureKind failureKind, string detail) =>
        new(false, failureKind.ToString(), detail);
}
