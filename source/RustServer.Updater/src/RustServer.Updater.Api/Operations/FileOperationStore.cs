using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Stores each operation as an independently recoverable JSON file.
/// </summary>
public sealed class FileOperationStore : IOperationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storagePath;
    private readonly ILogger<FileOperationStore> _logger;
    private readonly SemaphoreSlim _storeLock = new(1, 1);

    /// <summary>
    /// Initializes the file-backed operation store.
    /// </summary>
    public FileOperationStore(IOptions<OperationStoreOptions> options, ILogger<FileOperationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _storagePath = string.IsNullOrWhiteSpace(options.Value.StoragePath)
            ? throw new OptionsValidationException(
                OperationStoreOptions.SectionName,
                typeof(OperationStoreOptions),
                ["Operations:StoragePath must not be empty."])
            : Path.GetFullPath(options.Value.StoragePath);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task CreateAsync(Operation operation, CancellationToken cancellationToken) =>
        PersistAsync(operation, replaceExisting: false, cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(Operation operation, CancellationToken cancellationToken) =>
        PersistAsync(operation, replaceExisting: true, cancellationToken);

    /// <inheritdoc />
    public async Task<Operation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadOperationAsync(GetOperationPath(id), cancellationToken);
        }
        finally
        {
            _storeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var operations = await GetAllAsync(cancellationToken);
        return operations
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.Id)
            .Take(limit)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Operation>> GetIncompleteAsync(CancellationToken cancellationToken)
    {
        var operations = await GetAllAsync(cancellationToken);
        return operations
            .Where(operation => operation.Status is OperationStatus.Pending or OperationStatus.Running)
            .ToArray();
    }

    private async Task PersistAsync(Operation operation, bool replaceExisting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(operation);

        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_storagePath);
            var destinationPath = GetOperationPath(operation.Id);
            if (!replaceExisting && File.Exists(destinationPath))
            {
                throw new InvalidOperationException($"Operation '{operation.Id}' already exists.");
            }

            if (replaceExisting && !File.Exists(destinationPath))
            {
                throw new FileNotFoundException("The operation record to replace was not found.", destinationPath);
            }

            var temporaryPath = Path.Combine(_storagePath, $".{operation.Id:N}.{Guid.NewGuid():N}.tmp");
            try
            {
                await WriteTemporaryFileAsync(temporaryPath, operation, cancellationToken);
                ReplaceAtomically(temporaryPath, destinationPath, replaceExisting);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _storeLock.Release();
        }
    }

    private async Task<IReadOnlyCollection<Operation>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_storagePath))
            {
                return Array.Empty<Operation>();
            }

            var operations = new List<Operation>();
            foreach (var path in Directory.EnumerateFiles(_storagePath, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = await ReadOperationAsync(path, cancellationToken);
                if (operation is not null)
                {
                    operations.Add(operation);
                }
            }

            return operations;
        }
        finally
        {
            _storeLock.Release();
        }
    }

    private async Task<Operation?> ReadOperationAsync(string path, CancellationToken cancellationToken)
    {
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
            var operation = await JsonSerializer.DeserializeAsync<Operation>(stream, SerializerOptions, cancellationToken);
            if (operation is null)
            {
                throw new JsonException("The operation record is empty.");
            }

            ValidateOperation(operation);
            return operation;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        {
            _logger.LogWarning(exception, "Ignoring corrupt operation record at {OperationRecordPath}.", path);
            return null;
        }
    }

    private static async Task WriteTemporaryFileAsync(
        string temporaryPath,
        Operation operation,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, operation, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceAtomically(string temporaryPath, string destinationPath, bool replaceExisting)
    {
        if (!replaceExisting)
        {
            File.Move(temporaryPath, destinationPath);
            return;
        }

        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }

    private string GetOperationPath(Guid id) => Path.Combine(_storagePath, $"{id:N}.json");

    private static void ValidateOperation(Operation operation)
    {
        if (operation.Id == Guid.Empty)
        {
            throw new ArgumentException("Operation identifier must not be empty.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(operation.ApplicationName))
        {
            throw new ArgumentException("Operation application name must not be empty.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(operation.Step))
        {
            throw new ArgumentException("Operation step must not be empty.", nameof(operation));
        }

        if (!Enum.IsDefined(operation.Type) || !Enum.IsDefined(operation.Status))
        {
            throw new ArgumentException("Operation type or status is invalid.", nameof(operation));
        }

        if (operation.Type == OperationType.Prepare
            && operation.Status == OperationStatus.Succeeded
            && operation.Result is null)
        {
            throw new ArgumentException("A successful package preparation requires a result.", nameof(operation));
        }

        if (operation.Result is { } result
            && (string.IsNullOrWhiteSpace(result.ApplicationName)
                || string.IsNullOrWhiteSpace(result.ReleaseTag)
                || string.IsNullOrWhiteSpace(result.StagingPath)
                || result.VerifiedChecksum.Length != 64
                || !result.VerifiedChecksum.All(char.IsAsciiHexDigit)))
        {
            throw new ArgumentException("Operation preparation result is invalid.", nameof(operation));
        }

        if (operation.Deployment is { } deployment
            && deployment.RecordedAt == default)
        {
            throw new ArgumentException("Operation deployment result is invalid.", nameof(operation));
        }
    }
}
