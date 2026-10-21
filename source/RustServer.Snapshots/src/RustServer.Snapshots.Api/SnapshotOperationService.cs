using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class SnapshotOperationService : BackgroundService
{
    private readonly Channel<OperationState> _queue = Channel.CreateUnbounded<OperationState>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, OperationState> _operations = new();
    private readonly SnapshotArchiveService _archives;
    private readonly IControlServerClient _controlServer;
    private readonly ILogger<SnapshotOperationService> _logger;

    public SnapshotOperationService(
        SnapshotArchiveService archives,
        IControlServerClient controlServer,
        ILogger<SnapshotOperationService> logger)
    {
        _archives = archives;
        _controlServer = controlServer;
        _logger = logger;
    }

    public SnapshotOperationResponse QueueCreate(SnapshotType type) =>
        Queue(SnapshotOperationType.CreateSnapshot, type, snapshotId: null);

    public SnapshotOperationResponse QueueRestore(string snapshotId) =>
        Queue(SnapshotOperationType.RestoreSnapshot, snapshotType: null, snapshotId);

    public SnapshotOperationResponse? Get(string operationId) =>
        _operations.TryGetValue(operationId, out var operation) ? operation.ToResponse() : null;

    public bool HasActiveOperationForSnapshot(string snapshotId) =>
        _operations.Values.Any(operation => operation.IsActiveForSnapshot(snapshotId));

    public bool HasActiveCreateOperation() =>
        _operations.Values.Any(operation => operation.IsActiveCreate());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var operation in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ExecuteOperationAsync(operation, stoppingToken);
        }
    }

    private SnapshotOperationResponse Queue(
        SnapshotOperationType type,
        SnapshotType? snapshotType,
        string? snapshotId)
    {
        var operation = new OperationState(
            Guid.NewGuid().ToString("N"),
            type,
            snapshotType,
            snapshotId,
            DateTimeOffset.UtcNow);
        _operations[operation.Id] = operation;

        if (!_queue.Writer.TryWrite(operation))
        {
            _operations.TryRemove(operation.Id, out _);
            throw new InvalidOperationException("Snapshot operation queue is unavailable.");
        }

        return operation.ToResponse();
    }

    private async Task ExecuteOperationAsync(OperationState operation, CancellationToken cancellationToken)
    {
        operation.MarkRunning();

        try
        {
            switch (operation.Type)
            {
                case SnapshotOperationType.CreateSnapshot:
                    await CreateSnapshotAsync(operation, cancellationToken);
                    break;
                case SnapshotOperationType.RestoreSnapshot:
                    await RestoreSnapshotAsync(operation, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unknown snapshot operation type.");
            }

            operation.MarkSucceeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Snapshot operation {OperationId} failed.", operation.Id);
            operation.MarkFailed(GetPublicErrorMessage(exception));
        }
    }

    private async Task CreateSnapshotAsync(OperationState operation, CancellationToken cancellationToken)
    {
        var snapshotType = operation.SnapshotType
            ?? throw new InvalidOperationException("A snapshot type is required for a create operation.");

        // Fast is a best-effort filesystem capture: do not depend on Rust
        // commands or its RCON availability. Consistent still requires Stop.
        if (snapshotType == SnapshotType.Consistent)
        {
            await EnsureServerStoppedAsync(cancellationToken);
        }

        _archives.CreateSnapshot(snapshotType);
    }

    private async Task RestoreSnapshotAsync(OperationState operation, CancellationToken cancellationToken)
    {
        await EnsureServerStoppedAsync(cancellationToken);

        var snapshotId = operation.SnapshotId
            ?? throw new InvalidOperationException("A snapshot identifier is required for a restore operation.");
        var snapshot = _archives.GetSnapshot(snapshotId)
            ?? throw new SnapshotStorageException("The requested snapshot no longer exists.");
        _archives.RestoreSnapshot(snapshot);
    }

    private async Task EnsureServerStoppedAsync(CancellationToken cancellationToken)
    {
        if (await _controlServer.IsServerRunningAsync(cancellationToken))
        {
            throw new SnapshotStorageException("Rust server must be stopped before this snapshot operation can run.");
        }
    }

    private static string GetPublicErrorMessage(Exception exception) => exception switch
    {
        SnapshotConfigurationException or SnapshotStorageException or ControlServerUnavailableException => exception.Message,
        _ => "Snapshot operation failed."
    };

    private sealed class OperationState
    {
        private readonly object _sync = new();

        public OperationState(
            string id,
            SnapshotOperationType type,
            SnapshotType? snapshotType,
            string? snapshotId,
            DateTimeOffset createdAt)
        {
            Id = id;
            Type = type;
            SnapshotType = snapshotType;
            SnapshotId = snapshotId;
            CreatedAt = createdAt;
        }

        public string Id { get; }

        public SnapshotOperationType Type { get; }

        public SnapshotType? SnapshotType { get; }

        public string? SnapshotId { get; }

        public DateTimeOffset CreatedAt { get; }

        private SnapshotOperationStatus Status { get; set; } = SnapshotOperationStatus.Pending;

        private DateTimeOffset? StartedAt { get; set; }

        private DateTimeOffset? FinishedAt { get; set; }

        private string? Error { get; set; }

        public void MarkRunning()
        {
            lock (_sync)
            {
                Status = SnapshotOperationStatus.Running;
                StartedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkSucceeded()
        {
            lock (_sync)
            {
                Status = SnapshotOperationStatus.Succeeded;
                FinishedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkFailed(string error)
        {
            lock (_sync)
            {
                Status = SnapshotOperationStatus.Failed;
                FinishedAt = DateTimeOffset.UtcNow;
                Error = error;
            }
        }

        public bool IsActiveForSnapshot(string snapshotId)
        {
            lock (_sync)
            {
                return SnapshotId == snapshotId &&
                    Status is SnapshotOperationStatus.Pending or SnapshotOperationStatus.Running;
            }
        }

        public bool IsActiveCreate()
        {
            lock (_sync)
            {
                return Type == SnapshotOperationType.CreateSnapshot &&
                    Status is SnapshotOperationStatus.Pending or SnapshotOperationStatus.Running;
            }
        }

        public SnapshotOperationResponse ToResponse()
        {
            lock (_sync)
            {
                return new SnapshotOperationResponse(
                    Id,
                    Type,
                    Status,
                    CreatedAt,
                    StartedAt,
                    FinishedAt,
                    Error);
            }
        }
    }
}
