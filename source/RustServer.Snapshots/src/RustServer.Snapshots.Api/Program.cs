using Microsoft.OpenApi;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var snapshotsUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_SNAPSHOTS_URL");
var snapshotsBindHost = RequireIpv4BindHost(builder.Configuration, "RUSTSERVER_SNAPSHOTS_BIND_HOST");
var controlApiBaseUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_CONTROL_URL");

builder.WebHost.UseUrls($"http://{snapshotsBindHost}:{snapshotsUrl.Port}");

builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("openapi", new OpenApiInfo
    {
        Title = "RustServer Snapshots API",
        Version = "v1",
        Description = "Creates, lists, restores, and deletes Rust Dedicated Server runtime snapshots. Create and restore execute asynchronously."
    });
    options.MapType<SnapshotType>(() => StringEnumSchema("fast", "consistent"));
    options.MapType<SnapshotOperationType>(() => StringEnumSchema("createSnapshot", "restoreSnapshot"));
    options.MapType<SnapshotOperationStatus>(() => StringEnumSchema("pending", "running", "succeeded", "failed"));
    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();
    options.UseAllOfToExtendReferenceSchemas();
    options.IncludeXmlComments(Path.Combine(
        AppContext.BaseDirectory,
        "RustServer.Snapshots.Api.xml"));
});
var snapshotOptions = builder.Configuration
    .GetSection(SnapshotOptions.SectionName)
    .Get<SnapshotOptions>() ?? new SnapshotOptions();
var controlApiOptions = builder.Configuration
    .GetSection(ControlApiOptions.SectionName)
    .Get<ControlApiOptions>() ?? new ControlApiOptions();

controlApiOptions = new ControlApiOptions
{
    BaseUrl = controlApiBaseUrl.AbsoluteUri,
    Timeout = controlApiOptions.Timeout
};

if (controlApiOptions.Timeout <= TimeSpan.Zero)
{
    throw new InvalidOperationException("RustServerControlApi:Timeout must be greater than zero.");
}

builder.Services.AddSingleton(snapshotOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SnapshotArchiveService>();
builder.Services.AddHttpClient<IControlServerClient, ControlServerClient>(client =>
{
    client.BaseAddress = new Uri(controlApiOptions.BaseUrl, UriKind.Absolute);
    client.Timeout = controlApiOptions.Timeout;
});
builder.Services.AddSingleton<SnapshotOperationService>();
builder.Services.AddHostedService(services => services.GetRequiredService<SnapshotOperationService>());

var app = builder.Build();

app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi.json", "RustServer Snapshots API v1"));
}

app.MapGet("/health", () => Results.Ok())
    .WithTags("Health")
    .WithName("GetHealth")
    .WithSummary("Reports whether the Snapshots service is responding.")
    .WithDescription(
        "Returns 200 OK when the Snapshots service is running and able to respond. This endpoint does not check external dependencies or snapshot storage.")
    .Produces(StatusCodes.Status200OK);

var snapshots = app.MapGroup("/api/snapshots")
    .WithTags("Snapshots");

snapshots.MapPost("", async (
    CreateSnapshotRequest request,
    SnapshotArchiveService archives,
    SnapshotOperationService operations,
    IControlServerClient controlServer,
    CancellationToken cancellationToken) =>
    {
        try
        {
            archives.EnsureCreateReady();

            if (request.Type == SnapshotType.Consistent)
            {
                var preconditionError = await EnsureServerStoppedAsync(controlServer, cancellationToken);
                if (preconditionError is not null)
                {
                    return preconditionError;
                }
            }

            var operation = operations.QueueCreate(request.Type);
            return Results.Accepted(
                "/api/operations/" + operation.OperationId,
                new OperationAcceptedResponse(operation.OperationId, operation.Status));
        }
        catch (SnapshotConfigurationException exception)
        {
            return ConfigurationFailure(exception);
        }
        catch (SnapshotStorageException exception)
        {
            return StorageFailure(exception);
        }
    })
    .WithName("CreateSnapshot")
    .WithSummary("Queues creation of a Fast or Consistent snapshot.")
    .WithDescription(
        "A Fast snapshot copies currently persisted runtime files directly into an archive without RCON. " +
        "It can run while Rust is running, but unsaved changes may be absent and concurrent file writes can yield an inconsistent copy. " +
        "A Consistent snapshot is accepted only when RustServer.Control reports that the server is stopped; this service never stops or starts it. " +
        "Returns 202 Accepted with an operation identifier. Invalid input returns 400; an unsafe Consistent request returns 409 ServerMustBeStopped; invalid local storage configuration returns 500; unavailable Control status returns 502. Filesystem and archive failures are reported by the asynchronous operation.")
    .Accepts<CreateSnapshotRequest>("application/json")
    .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

snapshots.MapGet("", (SnapshotArchiveService archives) =>
    {
        try
        {
            var snapshots = archives.GetSnapshots()
                .Select(ToSummaryResponse)
                .ToArray();
            return Results.Ok(snapshots);
        }
        catch (SnapshotConfigurationException exception)
        {
            return ConfigurationFailure(exception);
        }
        catch (SnapshotStorageException exception)
        {
            return StorageFailure(exception);
        }
    })
    .WithName("ListSnapshots")
    .WithSummary("Lists snapshots, newest first.")
    .WithDescription(
        "Returns UI-ready snapshot summaries sorted by createdAt descending. createdAt is the human-readable identifier, while id is an opaque stable identifier for follow-up requests. " +
        "Returns 200 OK with an empty array when no snapshots exist, or 500 when local storage is unavailable or misconfigured.")
    .Produces<IReadOnlyList<SnapshotSummaryResponse>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

snapshots.MapGet("/{id}", (string id, SnapshotArchiveService archives) =>
    {
        try
        {
            var snapshot = archives.GetSnapshot(id);
            return snapshot is null
                ? Results.NotFound()
                : Results.Ok(ToSnapshotResponse(snapshot));
        }
        catch (SnapshotConfigurationException exception)
        {
            return ConfigurationFailure(exception);
        }
        catch (SnapshotStorageException exception)
        {
            return StorageFailure(exception);
        }
    })
    .WithName("GetSnapshot")
    .WithSummary("Gets one snapshot by its opaque stable identifier.")
    .WithDescription(
        "Returns the snapshot metadata for the supplied stable identifier. The initial detail model intentionally matches the list summary. " +
        "Returns 404 Not Found when the metadata or archive does not exist, or 500 when local storage is unavailable or misconfigured.")
    .Produces<SnapshotResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

snapshots.MapPost("/{id}/restore", async (
    string id,
    SnapshotArchiveService archives,
    SnapshotOperationService operations,
    IControlServerClient controlServer,
    CancellationToken cancellationToken) =>
    {
        try
        {
            archives.EnsureRestoreReady();
            if (archives.GetSnapshot(id) is null)
            {
                return Results.NotFound();
            }

            var preconditionError = await EnsureServerStoppedAsync(controlServer, cancellationToken);
            if (preconditionError is not null)
            {
                return preconditionError;
            }

            var operation = operations.QueueRestore(id);
            return Results.Accepted(
                "/api/operations/" + operation.OperationId,
                new OperationAcceptedResponse(operation.OperationId, operation.Status));
        }
        catch (SnapshotConfigurationException exception)
        {
            return ConfigurationFailure(exception);
        }
        catch (SnapshotStorageException exception)
        {
            return StorageFailure(exception);
        }
    })
    .WithName("RestoreSnapshot")
    .WithSummary("Queues destructive restoration of a snapshot.")
    .WithDescription(
        "Restore replaces the configured server identity and Oxide data, config, plugins, and language directories from the snapshot archive. Rust must be stopped according to RustServer.Control; this service never changes its lifecycle. " +
        "Returns 202 Accepted with an operation identifier, 404 Not Found for an absent snapshot, 409 ServerMustBeStopped if Rust is running, 500 for local storage/configuration errors, or 502 when Control status is unavailable.")
    .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

snapshots.MapDelete("/{id}", (string id, SnapshotArchiveService archives, SnapshotOperationService operations) =>
    {
        try
        {
            if (operations.HasActiveCreateOperation() || operations.HasActiveOperationForSnapshot(id))
            {
                return Results.Problem(
                    title: "SnapshotOperationInProgress",
                    detail: "The snapshot cannot be deleted while snapshot creation or its restore operation is pending or running.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return archives.DeleteSnapshot(id)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (SnapshotConfigurationException exception)
        {
            return ConfigurationFailure(exception);
        }
        catch (SnapshotStorageException exception)
        {
            return StorageFailure(exception);
        }
    })
    .WithName("DeleteSnapshot")
    .WithSummary("Deletes a snapshot archive and its metadata.")
    .WithDescription(
        "Deletes the archive and metadata identified by id. Returns 204 No Content on success, 404 Not Found when the snapshot is absent, or 409 SnapshotOperationInProgress while snapshot creation or that snapshot's restore is pending or running. " +
        "Returns 500 when local storage is unavailable or misconfigured.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

var operations = app.MapGroup("/api/operations")
    .WithTags("Operations");

operations.MapGet("/{operationId}", (string operationId, SnapshotOperationService operationService) =>
    operationService.Get(operationId) is { } operation
        ? Results.Ok(operation)
        : Results.NotFound())
    .WithName("GetSnapshotOperation")
    .WithSummary("Gets the current state of an asynchronous snapshot operation.")
    .WithDescription(
        "Create and restore operations progress through pending, running, succeeded, or failed. Operation state is in-process and is available until the service restarts. " +
        "Returns 200 OK for a known operation or 404 Not Found for an unknown identifier.")
    .Produces<SnapshotOperationResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

app.Run();

static string RequireIpv4BindHost(IConfiguration configuration, string key)
{
    var value = configuration[key]?.Trim();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    if (!System.Net.IPAddress.TryParse(value, out var address)
        || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        || address.ToString() != value)
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be a canonical IPv4 bind host.");
    }

    return value;
}

static Uri RequireHttpUrl(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        || !string.IsNullOrEmpty(uri.UserInfo)
        || uri.AbsolutePath != "/"
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be an absolute HTTP or HTTPS URL without credentials, a path, query, or fragment.");
    }

    return uri;
}

static SnapshotSummaryResponse ToSummaryResponse(SnapshotMetadata snapshot) =>
    new(snapshot.Id, snapshot.CreatedAt, snapshot.Type, snapshot.SizeBytes);

static SnapshotResponse ToSnapshotResponse(SnapshotMetadata snapshot) =>
    new(snapshot.Id, snapshot.CreatedAt, snapshot.Type, snapshot.SizeBytes);

static async Task<IResult?> EnsureServerStoppedAsync(
    IControlServerClient controlServer,
    CancellationToken cancellationToken)
{
    try
    {
        return await controlServer.IsServerRunningAsync(cancellationToken)
            ? Results.Problem(
                title: "ServerMustBeStopped",
                detail: "Stop the Rust server through RustServer.Control before starting this snapshot operation.",
                statusCode: StatusCodes.Status409Conflict)
            : null;
    }
    catch (ControlServerUnavailableException)
    {
        return Results.Problem(
            title: "RustServerControlUnavailable",
            detail: "RustServer.Control server status could not be determined.",
            statusCode: StatusCodes.Status502BadGateway);
    }
}

static IResult ConfigurationFailure(SnapshotConfigurationException exception) =>
    Results.Problem(
        title: "Snapshot service is not configured",
        detail: exception.Message,
        statusCode: StatusCodes.Status500InternalServerError);

static IResult StorageFailure(SnapshotStorageException exception) =>
    Results.Problem(
        title: "Snapshot storage is unavailable",
        detail: exception.Message,
        statusCode: StatusCodes.Status500InternalServerError);

static OpenApiSchema StringEnumSchema(params string[] values) => new()
{
    Type = JsonSchemaType.String,
    Enum = values.Select(value => (JsonNode) JsonValue.Create(value)!).ToList()
};

public partial class Program;

/// <summary>
/// Requested consistency mode for a snapshot operation.
/// </summary>
public enum SnapshotType
{
    Fast,
    Consistent
}

/// <summary>
/// Request to create a snapshot. The service assigns the identifier and creation time.
/// </summary>
/// <param name="Type">Requested snapshot consistency mode.</param>
public sealed record CreateSnapshotRequest([property: JsonRequired] SnapshotType Type);

/// <summary>
/// Snapshot data suitable for a list view.
/// </summary>
/// <param name="Id">Opaque stable snapshot identifier. It is not a timestamp and clients must not infer its format.</param>
/// <param name="CreatedAt">UTC time at which the snapshot was created. This is the primary human-readable UI identifier.</param>
/// <param name="Type">Consistency mode used to create the snapshot.</param>
/// <param name="SizeBytes">Measured archive size in bytes.</param>
public sealed record SnapshotSummaryResponse(
    string Id,
    DateTimeOffset CreatedAt,
    SnapshotType Type,
    long SizeBytes);

/// <summary>
/// Detail for one snapshot. The current detail intentionally matches the list summary.
/// </summary>
/// <param name="Id">Opaque stable snapshot identifier. It is not a timestamp and clients must not infer its format.</param>
/// <param name="CreatedAt">UTC time at which the snapshot was created.</param>
/// <param name="Type">Consistency mode used to create the snapshot.</param>
/// <param name="SizeBytes">Measured archive size in bytes.</param>
public sealed record SnapshotResponse(
    string Id,
    DateTimeOffset CreatedAt,
    SnapshotType Type,
    long SizeBytes);

/// <summary>
/// Snapshot operation accepted for asynchronous processing.
/// </summary>
/// <param name="OperationId">Opaque operation identifier used with <c>GET /api/operations/{operationId}</c>.</param>
/// <param name="Status">Initial operation status.</param>
public sealed record OperationAcceptedResponse(
    string OperationId,
    SnapshotOperationStatus Status);

/// <summary>
/// Kind of asynchronous snapshot operation.
/// </summary>
public enum SnapshotOperationType
{
    CreateSnapshot,
    RestoreSnapshot
}

/// <summary>
/// Lifecycle status for an asynchronous snapshot operation.
/// </summary>
public enum SnapshotOperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// Current state of an asynchronous snapshot operation.
/// </summary>
/// <param name="OperationId">Opaque operation identifier.</param>
/// <param name="Type">Snapshot operation kind.</param>
/// <param name="Status">Current operation status.</param>
/// <param name="CreatedAt">UTC time at which the operation was created.</param>
/// <param name="StartedAt">UTC time at which execution began, or <see langword="null"/> while pending.</param>
/// <param name="FinishedAt">UTC time at which execution finished, or <see langword="null"/> until completion.</param>
/// <param name="Error">Failure detail, or <see langword="null"/> when the operation has not failed.</param>
public sealed record SnapshotOperationResponse(
    string OperationId,
    SnapshotOperationType Type,
    SnapshotOperationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);
