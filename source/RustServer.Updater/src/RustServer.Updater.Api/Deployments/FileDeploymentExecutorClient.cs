using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RustServer.Updater.Api.Deployments;

/// <summary>
/// Communicates with the root executor through fixed local files and its fixed systemd socket.
/// No API-provided path, unit name, command, or argument is forwarded across the privilege boundary.
/// </summary>
public sealed class FileDeploymentExecutorClient : IDeploymentExecutorClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _requestStoragePath;
    private readonly string _transactionStoragePath;
    private readonly string _socketPath;

    /// <summary>Creates the production client from fixed server-side configuration.</summary>
    public FileDeploymentExecutorClient(IOptions<DeploymentExecutorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _requestStoragePath = RequireAbsolutePath(value.RequestStoragePath, nameof(value.RequestStoragePath));
        _transactionStoragePath = RequireAbsolutePath(value.TransactionStoragePath, nameof(value.TransactionStoragePath));
        _socketPath = RequireAbsolutePath(value.SocketPath, nameof(value.SocketPath));
    }

    /// <inheritdoc />
    public async Task<DeploymentTransaction> ExecuteAsync(
        DeploymentExecutorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty)
        {
            throw new ArgumentException("Deployment operation identifier must not be empty.", nameof(request));
        }

        await WriteRequestAsync(request, cancellationToken);
        await SignalExecutorAsync(request.OperationId, cancellationToken);
        var transaction = await GetTransactionAsync(request.OperationId, cancellationToken);
        if (transaction is not null)
        {
            return transaction;
        }

        throw new DeploymentActivationException("The deployment executor finished without a durable transaction result.");
    }

    private async Task SignalExecutorAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new DeploymentActivationException("The deployment executor socket is supported only on Linux.");
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cancellationToken);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            var operationLine = Encoding.ASCII.GetBytes($"{operationId:D}\n");
            await stream.WriteAsync(operationLine, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            // Socket activation owns the connection until the root worker has a
            // terminal transaction. The worker does not accept a payload beyond
            // the UUID, and connection close is its completion acknowledgement.
            var acknowledgement = new byte[1];
            while (await stream.ReadAsync(acknowledgement, cancellationToken) != 0)
            {
            }
        }
        catch (SocketException exception)
        {
            throw new DeploymentActivationException($"Could not signal the deployment executor socket: {exception.SocketErrorCode}.");
        }
    }

    /// <inheritdoc />
    public async Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            return null;
        }

        var path = Path.Combine(_transactionStoragePath, $"{operationId:N}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<DeploymentTransaction>(stream, SerializerOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteRequestAsync(DeploymentExecutorRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_requestStoragePath);
        var destination = Path.Combine(_requestStoragePath, $"{request.OperationId:N}.json");
        var temporary = Path.Combine(_requestStoragePath, $".{request.OperationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, request, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string RequireAbsolutePath(string path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!path.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path)))
        {
            throw new OptionsValidationException(
                DeploymentExecutorOptions.SectionName,
                typeof(DeploymentExecutorOptions),
                [$"{DeploymentExecutorOptions.SectionName}:{settingName} must be absolute."]);
        }

        return Path.GetFullPath(path);
    }
}
