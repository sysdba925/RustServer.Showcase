using System.Text.Json;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Infrastructure.NetworkAccess;

/// <summary>
/// Stores NetworkAccess settings in a Control-owned JSON file using atomic replacement.
/// </summary>
public sealed class FileNetworkAccessSettingsStore : INetworkAccessSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _storagePath;

    public FileNetworkAccessSettingsStore(NetworkAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _storagePath = string.IsNullOrWhiteSpace(options.StoragePath)
            ? NetworkAccessOptions.DefaultStoragePath
            : Path.GetFullPath(options.StoragePath);
    }

    public NetworkAccessSettings Get()
    {
        lock (_sync)
        {
            if (!File.Exists(_storagePath))
            {
                return NetworkAccessSettings.Default;
            }

            try
            {
                return JsonSerializer.Deserialize<NetworkAccessSettings>(
                        File.ReadAllText(_storagePath),
                        SerializerOptions)
                    ?? throw new JsonException("NetworkAccess settings are missing.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new NetworkAccessSettingsStoreException(
                    "Control-owned NetworkAccess settings could not be read.",
                    exception);
            }
        }
    }

    public void Save(NetworkAccessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(_storagePath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new IOException("The NetworkAccess storage directory is invalid.");
                }

                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
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
                throw new NetworkAccessSettingsStoreException(
                    "Control-owned NetworkAccess settings could not be saved.",
                    exception);
            }
        }
    }
}
