using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class SqliteOperationHistoryStore : IOperationHistoryStore
{
    private const long SchemaVersion = 1;
    private readonly object initializationLock = new();
    private readonly object writeLock = new();
    private readonly string databasePath;
    private readonly JsonSerializerOptions serializerOptions;
    private bool initialized;

    public SqliteOperationHistoryStore(IOptions<OperationHistoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        databasePath = Path.GetFullPath(options.Value.DatabasePath);
        serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new OperationSourceJsonConverter());
        serializerOptions.Converters.Add(new OperationReportJsonConverter());
    }

    public void Save(OperationReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        lock (writeLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();
            try
            {
                using var connection = OpenConnection();
                SaveCore(connection, report);
            }
            catch (SqliteException exception)
            {
                throw Unavailable(exception);
            }
        }
    }

    public OperationReport? Get(OperationId operationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT report_json FROM operations WHERE operation_id = $operationId;";
            command.Parameters.AddWithValue("$operationId", operationId.Value.ToString("D"));
            var json = command.ExecuteScalar() as string;
            return json is null ? null : Deserialize(json);
        }
        catch (SqliteException exception)
        {
            throw Unavailable(exception);
        }
    }

    public OperationHistoryPage List(int limit, string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperationHistoryPagination.IsValidLimit(limit))
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        long cursorTicks = 0;
        Guid cursorOperationId = default;
        if (cursor is not null && !OperationHistoryCursor.TryDecode(cursor, out cursorTicks, out cursorOperationId))
        {
            throw new OperationHistoryCursorException();
        }

        EnsureInitialized();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = cursor is null
                ? """
                  SELECT operation_id, type, status, started_at_ticks, completed_at_ticks, changed_files
                  FROM operations
                  ORDER BY started_at_ticks DESC, operation_id DESC
                  LIMIT $take;
                  """
                : """
                  SELECT operation_id, type, status, started_at_ticks, completed_at_ticks, changed_files
                  FROM operations
                  WHERE started_at_ticks < $startedAtTicks
                     OR (started_at_ticks = $startedAtTicks AND operation_id < $operationId)
                  ORDER BY started_at_ticks DESC, operation_id DESC
                  LIMIT $take;
                  """;
            command.Parameters.AddWithValue("$take", limit + 1);
            if (cursor is not null)
            {
                command.Parameters.AddWithValue("$startedAtTicks", cursorTicks);
                command.Parameters.AddWithValue("$operationId", cursorOperationId.ToString("D"));
            }

            using var reader = command.ExecuteReader();
            var items = new List<OperationHistoryItem>(limit + 1);
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operationId = new OperationId(Guid.Parse(reader.GetString(0)));
                var startedAt = new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero);
                items.Add(new OperationHistoryItem(
                    operationId,
                    (OperationType) reader.GetInt64(1),
                    (OperationStatus) reader.GetInt64(2),
                    startedAt,
                    reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
                    reader.GetInt32(5)));
            }

            var hasMore = items.Count > limit;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            var last = items.LastOrDefault();
            return new OperationHistoryPage(
                items,
                hasMore && last is not null
                    ? OperationHistoryCursor.Encode(last.StartedAt.UtcTicks, last.OperationId.Value)
                    : null);
        }
        catch (SqliteException exception)
        {
            throw Unavailable(exception);
        }
    }

    public void MarkInProgressAsInterrupted(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            EnsureInitialized();
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT report_json FROM operations WHERE status = $inProgress;";
                command.Parameters.AddWithValue("$inProgress", (int) OperationStatus.InProgress);
                using var reader = command.ExecuteReader();
                var reports = new List<OperationReport>();
                while (reader.Read())
                {
                    reports.Add(Deserialize(reader.GetString(0)).MarkInterrupted(DateTimeOffset.UtcNow));
                }

                reader.Close();
                foreach (var report in reports)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SaveCore(connection, report);
                }
            }
            catch (SqliteException exception)
            {
                throw Unavailable(exception);
            }
        }
    }

    private void EnsureInitialized()
    {
        lock (initializationLock)
        {
            if (initialized)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(databasePath);
                if (string.IsNullOrEmpty(directory))
                {
                    throw new OperationHistoryException("The operation history database path must include a directory.");
                }

                Directory.CreateDirectory(directory);
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var versionCommand = connection.CreateCommand();
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "PRAGMA user_version;";
                var currentVersion = Convert.ToInt64(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                if (currentVersion > SchemaVersion)
                {
                    throw new OperationHistoryException("The operation history database uses a newer unsupported schema version.");
                }

                if (currentVersion == 0)
                {
                    using var schemaCommand = connection.CreateCommand();
                    schemaCommand.Transaction = transaction;
                    schemaCommand.CommandText = """
                        CREATE TABLE operations (
                            operation_id TEXT NOT NULL PRIMARY KEY,
                            type INTEGER NOT NULL,
                            status INTEGER NOT NULL,
                            started_at_ticks INTEGER NOT NULL,
                            completed_at_ticks INTEGER NULL,
                            changed_files INTEGER NOT NULL,
                            report_json TEXT NOT NULL
                        );
                        CREATE INDEX ix_operations_started_at_operation_id
                            ON operations (started_at_ticks DESC, operation_id DESC);
                        PRAGMA user_version = 1;
                        """;
                    schemaCommand.ExecuteNonQuery();
                }

                transaction.Commit();
                initialized = true;
            }
            catch (OperationHistoryException)
            {
                throw;
            }
            catch (SqliteException exception)
            {
                throw Unavailable(exception);
            }
            catch (IOException exception)
            {
                throw new OperationHistoryException("The operation history database path is unavailable.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new OperationHistoryException("The operation history database path is unavailable.", exception);
            }
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
        pragmaCommand.ExecuteNonQuery();
        return connection;
    }

    private void SaveCore(SqliteConnection connection, OperationReport report)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operations (
                operation_id, type, status, started_at_ticks, completed_at_ticks, changed_files, report_json)
            VALUES (
                $operationId, $type, $status, $startedAtTicks, $completedAtTicks, $changedFiles, $reportJson)
            ON CONFLICT(operation_id) DO UPDATE SET
                type = excluded.type,
                status = excluded.status,
                started_at_ticks = excluded.started_at_ticks,
                completed_at_ticks = excluded.completed_at_ticks,
                changed_files = excluded.changed_files,
                report_json = excluded.report_json;
            """;
        command.Parameters.AddWithValue("$operationId", report.Id.Value.ToString("D"));
        command.Parameters.AddWithValue("$type", (int) report.Type);
        command.Parameters.AddWithValue("$status", (int) report.Status);
        command.Parameters.AddWithValue("$startedAtTicks", report.StartedAt.UtcTicks);
        command.Parameters.AddWithValue("$completedAtTicks", report.CompletedAt?.UtcTicks is { } completedAtTicks
            ? completedAtTicks
            : DBNull.Value);
        command.Parameters.AddWithValue("$changedFiles", report.Changes.Count);
        command.Parameters.AddWithValue("$reportJson", JsonSerializer.Serialize(report, serializerOptions));
        command.ExecuteNonQuery();
    }

    private OperationReport Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OperationReport>(json, serializerOptions)
                ?? throw new OperationHistoryException("The operation history database contains an empty report.");
        }
        catch (JsonException exception)
        {
            throw new OperationHistoryException("The operation history database contains an unreadable report.", exception);
        }
    }

    private static OperationHistoryException Unavailable(SqliteException exception) =>
        new("The operation history database is unavailable or corrupted.", exception);

    private sealed class OperationSourceJsonConverter : JsonConverter<Source>
    {
        public override Source Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var kind = (SourceKind)root.GetProperty("kind").GetInt32();
            return kind switch
            {
                SourceKind.Development => Source.Development,
                SourceKind.Release => Source.ForRelease(new Release(root.GetProperty("release").GetProperty("version").GetString()
                    ?? throw new JsonException("Operation source release version is missing."))),
                _ => throw new JsonException("Operation source kind is unsupported.")
            };
        }

        public override void Write(Utf8JsonWriter writer, Source value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("kind", (int) value.Kind);
            if (value.Release is not null)
            {
                writer.WritePropertyName("release");
                writer.WriteStartObject();
                writer.WriteString("version", value.Release.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }

    private sealed class OperationReportJsonConverter : JsonConverter<OperationReport>
    {
        public override OperationReport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var id = new OperationId(root.GetProperty("id").GetProperty("value").GetGuid());
            return new OperationReport(
                id,
                (OperationType) root.GetProperty("type").GetInt32(),
                (OperationStatus) root.GetProperty("status").GetInt32(),
                root.GetProperty("currentPhase").GetString()
                    ?? throw new JsonException("Operation report phase is missing."),
                GetOptional<Source>(root, "source", options),
                GetRequiredList<OperationStep>(root, "steps", options),
                GetOptional<OperationFailure>(root, "failure", options),
                GetOptional<RollbackResult>(root, "rollback", options),
                GetOptional<BaselineModel>(root, "previousBaseline", options),
                GetOptional<BaselineModel>(root, "finalBaseline", options),
                GetRequiredList<OperationGitState>(root, "finalGitStates", options),
                GetRequiredList<OperationFileChange>(root, "changes", options),
                GetOptional<OperationSyncDetails>(root, "sync", options),
                root.GetProperty("startedAt").GetDateTimeOffset(),
                GetOptionalDateTimeOffset(root, "completedAt"));
        }

        public override void Write(Utf8JsonWriter writer, OperationReport value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new
            {
                value.Id,
                value.Type,
                value.Status,
                value.CurrentPhase,
                value.Source,
                value.Steps,
                value.Failure,
                value.Rollback,
                value.PreviousBaseline,
                value.FinalBaseline,
                value.FinalGitStates,
                value.Changes,
                value.Sync,
                value.StartedAt,
                value.CompletedAt
            }, options);

        private static T? GetOptional<T>(JsonElement root, string propertyName, JsonSerializerOptions options)
            where T : class
        {
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null)
            {
                return null;
            }

            return value.Deserialize<T>(options)
                ?? throw new JsonException($"Operation report property '{propertyName}' is invalid.");
        }

        private static IReadOnlyList<T> GetRequiredList<T>(
            JsonElement root,
            string propertyName,
            JsonSerializerOptions options)
        {
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null)
            {
                return [];
            }

            return value.Deserialize<List<T>>(options)
                ?? throw new JsonException($"Operation report property '{propertyName}' is invalid.");
        }

        private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out var value) && value.ValueKind is not JsonValueKind.Null
                ? value.GetDateTimeOffset()
                : null;
    }
}

public sealed class OperationHistoryCursorException : Exception;
