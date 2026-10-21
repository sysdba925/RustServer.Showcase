using System.Text.Json;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Stores desired and effective configuration in one Control-owned JSON file.
/// Writes replace the file atomically within its directory.
/// </summary>
public sealed class FileServerConfigurationStore : IServerConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _storagePath;
    private readonly ServerConfiguration _initialDesired;

    public FileServerConfigurationStore(ServerConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _storagePath = string.IsNullOrWhiteSpace(options.StoragePath)
            ? ServerConfigurationOptions.DefaultStoragePath
            : Path.GetFullPath(options.StoragePath);
        _initialDesired = options.CreateInitialDesired();
    }

    public ServerConfigurationState Get()
    {
        lock (_sync)
        {
            return ReadState();
        }
    }

    public void SetDesired(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_sync)
        {
            var current = ReadState();
            WriteState(current with { Desired = configuration });
        }
    }

    public void RecordEffective(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_sync)
        {
            var current = ReadState();
            WriteState(current with { Effective = configuration });
        }
    }

    private ServerConfigurationState ReadState()
    {
        if (!File.Exists(_storagePath))
        {
            return new ServerConfigurationState(_initialDesired, null);
        }

        try
        {
            var value = JsonSerializer.Deserialize<ServerConfigurationState>(
                File.ReadAllText(_storagePath),
                SerializerOptions);
            if (value?.Desired is null)
            {
                throw new JsonException("The desired configuration is missing.");
            }

            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ServerConfigurationStoreException(
                "Control-owned server configuration could not be read.",
                exception);
        }
    }

    private void WriteState(ServerConfigurationState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new IOException("The configuration storage directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, SerializerOptions));
                File.Move(temporaryPath, _storagePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ServerConfigurationStoreException(
                "Control-owned server configuration could not be saved.",
                exception);
        }
    }
}
