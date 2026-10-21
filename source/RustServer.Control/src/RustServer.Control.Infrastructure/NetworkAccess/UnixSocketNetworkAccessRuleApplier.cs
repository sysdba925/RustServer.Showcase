using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Infrastructure.NetworkAccess;

/// <summary>
/// Requests a narrowly scoped rule change from the root-owned local NetworkAccess helper.
/// </summary>
public sealed class UnixSocketNetworkAccessRuleApplier(NetworkAccessOptions options)
    : INetworkAccessRuleApplier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _socketPath = GetSocketPath(options);

    public async Task ApplyAsync(NetworkAccessSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cancellationToken);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);

            var request = new NetworkAccessHelperRequest(settings.PublicEnabled, settings.MaintenancePort);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions));
            await writer.FlushAsync(cancellationToken);

            var line = await reader.ReadLineAsync(cancellationToken);
            var response = string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<NetworkAccessHelperResponse>(line, SerializerOptions);
            if (response is null)
            {
                throw new NetworkAccessRuleApplicationException(
                    NetworkAccessRuleFailureKind.HelperUnavailable,
                    "The NetworkAccess helper returned an invalid response.");
            }

            if (!response.Succeeded)
            {
                throw new NetworkAccessRuleApplicationException(
                    ParseFailureKind(response.FailureKind),
                    string.IsNullOrWhiteSpace(response.Detail)
                        ? "The NetworkAccess helper did not apply the requested rules."
                        : response.Detail);
            }
        }
        catch (NetworkAccessRuleApplicationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException or IOException or JsonException)
        {
            throw new NetworkAccessRuleApplicationException(
                NetworkAccessRuleFailureKind.HelperUnavailable,
                "The privileged NetworkAccess helper is unavailable.",
                exception);
        }
    }

    private static string GetSocketPath(NetworkAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = string.IsNullOrWhiteSpace(options.HelperSocketPath)
            ? NetworkAccessOptions.DefaultHelperSocketPath
            : options.HelperSocketPath;

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NetworkAccess helper socket path must be an absolute Unix path.");
        }

        return path;
    }

    private static NetworkAccessRuleFailureKind ParseFailureKind(string? value) =>
        Enum.TryParse<NetworkAccessRuleFailureKind>(value, ignoreCase: true, out var kind)
            ? kind
            : NetworkAccessRuleFailureKind.ApplyFailed;
}
