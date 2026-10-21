using Microsoft.OpenApi;
using Microsoft.Extensions.Logging;
using RustServer.Control.Api.NetworkAccess;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;
using RustServer.Control.Application.StartupTracking;
using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Domain.StartupTracking;
using RustServer.Control.Domain.Wipe;
using RustServer.Control.Infrastructure.Operations;
using RustServer.Control.Infrastructure.NetworkAccess;
using RustServer.Control.Infrastructure.Processes;
using RustServer.Control.Infrastructure.Servers;
using RustServer.Control.Infrastructure.StartupTracking;
using RustServer.Control.Infrastructure.Wipe;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Serialization;

if (args is ["--network-access-helper"])
{
    using var shutdown = new CancellationTokenSource();
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };
    var helperSocketPath = Environment.GetEnvironmentVariable("NetworkAccess__HelperSocketPath");
    helperSocketPath = string.IsNullOrWhiteSpace(helperSocketPath)
        ? NetworkAccessOptions.DefaultHelperSocketPath
        : helperSocketPath;
    var configurator = new NftablesNetworkConfigurator(
        new SystemProcessExecutor(),
        loggerFactory.CreateLogger<NftablesNetworkConfigurator>());
    var helperHost = new NetworkAccessHelperHost(
        helperSocketPath,
        configurator,
        loggerFactory.CreateLogger<NetworkAccessHelperHost>());
    await helperHost.RunAsync(shutdown.Token);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var controlUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_CONTROL_URL");
string controlBindHost = RequireIpv4BindHost(builder.Configuration, "RUSTSERVER_CONTROL_BIND_HOST");
var rconGrpcUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_RCON_GRPC_URL");
var rconPort = new RustServerRconPort(RequireWebSocketUrl(builder.Configuration, "RUSTSERVER_RCON_URL").Port);

builder.WebHost.UseUrls($"http://{controlBindHost}:{controlUrl.Port}");

builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<WipeAction>(
            System.Text.Json.JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("openapi", new OpenApiInfo
    {
        Title = "RustServer Control API",
        Version = "v1",
        Description = "Controls Rust Dedicated Server lifecycle and manages Rust and Oxide update operations."
    });
    options.MapType<StartupMilestoneStateResponse>(() => new OpenApiSchema
    {
        Type = JsonSchemaType.String,
        Enum =
        [
            System.Text.Json.Nodes.JsonValue.Create("pending"),
            System.Text.Json.Nodes.JsonValue.Create("reached"),
            System.Text.Json.Nodes.JsonValue.Create("skipped")
        ]
    });
    options.SchemaFilter<WipeActionSchemaFilter>();
    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();
    options.UseAllOfToExtendReferenceSchemas();
    options.IncludeXmlComments(Path.Combine(
        AppContext.BaseDirectory,
        "RustServer.Control.Api.xml"));
});
builder.Services.AddSingleton(builder.Configuration
    .GetSection(RustServerControlOptions.SectionName)
    .Get<RustServerControlOptions>() ?? new RustServerControlOptions());
builder.Services.AddSingleton(builder.Configuration
    .GetSection(RustServerUpdateOptions.SectionName)
    .Get<RustServerUpdateOptions>() ?? new RustServerUpdateOptions());
builder.Services.AddSingleton(builder.Configuration
    .GetSection(OxideUpdateOptions.SectionName)
    .Get<OxideUpdateOptions>() ?? new OxideUpdateOptions());
builder.Services.AddSingleton(builder.Configuration
    .GetSection(ServerConfigurationOptions.SectionName)
    .Get<ServerConfigurationOptions>() ?? new ServerConfigurationOptions());
builder.Services.AddSingleton(builder.Configuration
    .GetSection(NetworkAccessOptions.SectionName)
    .Get<NetworkAccessOptions>() ?? new NetworkAccessOptions());
builder.Services.AddSingleton(rconPort);
builder.Services.AddSingleton<IProcessExecutor, SystemProcessExecutor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOperationStore, InMemoryOperationStore>();
builder.Services.AddSingleton<IServerMutationLock, ServerMutationLock>();
builder.Services.AddSingleton<IServerConfigurationStore, FileServerConfigurationStore>();
builder.Services.AddSingleton<INetworkAccessSettingsStore, FileNetworkAccessSettingsStore>();
builder.Services.AddSingleton<INetworkAccessRuleApplier, UnixSocketNetworkAccessRuleApplier>();
builder.Services.AddSingleton<NetworkAccessOrchestrator>();
builder.Services.AddSingleton<GetNetworkAccessQueryHandler>();
builder.Services.AddSingleton<UpdateNetworkAccessCommandHandler>();
builder.Services.AddSingleton<IServerControl, DockerComposeServerControl>();
builder.Services.AddSingleton<IStartupLogSource, DockerComposeStartupLogSource>();
builder.Services.AddSingleton<IStartupTrackingService, StartupTrackingService>();
builder.Services.AddSingleton<IRustServerUpdater, RustServerUpdater>();
builder.Services.AddSingleton<IOxideUpdater, OxideUpdater>();
builder.Services.AddRconGrpcClient(rconGrpcUrl);
builder.Services.AddSingleton<IRustUpdateOperationQueue, InMemoryRustUpdateOperationQueue>();
builder.Services.AddSingleton<IRustUpdateOperationService, RustUpdateOperationService>();
builder.Services.AddSingleton<IOxideUpdateOperationQueue, InMemoryOxideUpdateOperationQueue>();
builder.Services.AddSingleton<IOxideUpdateOperationService, OxideUpdateOperationService>();
builder.Services.AddSingleton<IWipeOperationQueue, InMemoryWipeOperationQueue>();
builder.Services.AddSingleton<IWipeEngine, WipeEngine>();
builder.Services.AddSingleton<IWipeOperationService, WipeOperationService>();
builder.Services.AddHostedService<RustUpdateOperationWorker>();
builder.Services.AddHostedService<OxideUpdateOperationWorker>();
builder.Services.AddHostedService<WipeOperationWorker>();
builder.Services.AddHostedService<NetworkAccessReconciliationService>();

var app = builder.Build();
var server = app.MapGroup("/api/server");

app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");

app.MapGet("/health", () => Results.Ok())
    .WithName("GetHealth")
    .WithSummary("Reports that the Control API is responding.")
    .Produces(StatusCodes.Status200OK);

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi.json", "RustServer Control API v1"));

    server.MapPost("/command", async (
        ServerCommandRequest? request,
        IRconClient commandClient,
        CancellationToken cancellationToken) =>
        await ExecuteServerCommandAsync(request, commandClient, cancellationToken))
        .WithName("ExecuteServerCommand");
}

server.MapGet("/status", (IServerControl control, CancellationToken cancellationToken) =>
    ExecuteControlOperationAsync(control.GetStatusAsync, cancellationToken))
    .WithName("GetServerStatus")
    .WithSummary("Gets Docker lifecycle and Rust server readiness state.")
    .Produces<ServerStatus>(StatusCodes.Status200OK);

server.MapGet("/configuration", (IServerConfigurationStore configurationStore) =>
    ExecuteConfigurationRead(configurationStore))
    .WithName("GetServerConfiguration")
    .WithSummary("Gets desired configuration and the configuration effective for the current Rust process.")
    .Produces<ServerConfigurationResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

server.MapPut("/configuration", (
    UpdateServerConfigurationRequest request,
    IServerConfigurationStore configurationStore) =>
    ExecuteConfigurationUpdate(request, configurationStore))
    .WithName("UpdateServerConfiguration")
    .WithSummary("Updates Control's desired game-server configuration without restarting Rust.")
    .Produces<ServerConfigurationResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status500InternalServerError);

server.MapNetworkAccessEndpoints();

var startup = server.MapGroup("/startup");

startup.MapGet("/schema", (IStartupTrackingService trackingService) =>
    Results.Ok(StartupSchemaResponse.From(trackingService.GetSchema())))
    .WithName("GetStartupTrackingSchema")
    .WithSummary("Gets the fixed Rust server startup milestone schema.")
    .Produces<StartupSchemaResponse>(StatusCodes.Status200OK);

startup.MapGet("", (IStartupTrackingService trackingService, CancellationToken cancellationToken) =>
    ExecuteStartupTrackingOperationAsync(trackingService.GetSnapshotAsync, cancellationToken))
    .WithName("GetStartupTrackingSnapshot")
    .WithSummary("Gets startup progress for the current Rust server run.")
    .Produces<StartupSnapshotResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

server.MapPost("/start", (
    IServerControl control,
    IOperationStore operationStore,
    CancellationToken cancellationToken) =>
    ExecuteLifecycleMutationAsync(control.StartAsync, operationStore, cancellationToken))
    .WithName("StartServer")
    .WithSummary("Starts the Rust server.")
    .Produces<ServerStatus>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);

server.MapPost("/stop", (
    IServerControl control,
    IOperationStore operationStore,
    CancellationToken cancellationToken) =>
    ExecuteLifecycleMutationAsync(control.StopAsync, operationStore, cancellationToken))
    .WithName("StopServer")
    .WithSummary("Stops the Rust server.")
    .Produces<ServerStatus>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status409Conflict);

server.MapPost("/restart", (
    IServerControl control,
    IOperationStore operationStore,
    CancellationToken cancellationToken) =>
    ExecuteLifecycleMutationAsync(control.RestartAsync, operationStore, cancellationToken))
    .WithName("RestartServer")
    .WithSummary("Restarts the Rust server.")
    .Produces<ServerStatus>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);

server.MapPost("/wipe", async (
    WipePlanRequest request,
    IWipeOperationService wipeOperationService,
    CancellationToken cancellationToken) =>
    {
        var plan = request.ToDomain();
        try
        {
            var queueResult = await wipeOperationService.QueueAsync(plan, cancellationToken);
            if (queueResult.Status == WipeOperationQueueStatus.ServerRunning)
            {
                return Results.Problem(
                    title: "Rust server is running",
                    detail: "Stop the Rust server before starting a wipe.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (queueResult.Status == WipeOperationQueueStatus.OperationInProgress)
            {
                return Results.Problem(
                    title: "Operation already in progress",
                    detail: "A server-mutating operation is already pending or running.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (queueResult.Status == WipeOperationQueueStatus.InvalidPlan)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["plan"] = [queueResult.ValidationError ?? "The wipe plan is invalid."]
                    });
            }

            var operation = queueResult.Operation
                ?? throw new InvalidOperationException("An accepted wipe operation is required.");
            string location = $"/api/operations/{operation.Id}";
            return Results.Accepted(location, OperationAcceptedResponse.From(operation));
        }
        catch (RustServerControlConfigurationException)
        {
            return Results.Problem(
                title: "Rust server control is not configured",
                detail: "Rust server control configuration is incomplete or invalid.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (RustServerControlException)
        {
            return Results.Problem(
                title: "Unable to determine Rust server status",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (ProcessExecutionException)
        {
            return Results.Problem(
                title: "Unable to start Docker Compose",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .WithName("WipeServer")
    .WithSummary("Queues an explicit wipe operation. Rust must remain stopped while it runs.")
    .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

server.MapPost("/update", async (
    IRustUpdateOperationService updateOperationService,
    CancellationToken cancellationToken) =>
    {
        try
        {
            var queueResult = await updateOperationService.QueueAsync(cancellationToken);
            if (queueResult.Status == RustUpdateOperationQueueStatus.ServerRunning)
            {
                return Results.Problem(
                    title: "Rust server is running",
                    detail: "Stop the Rust server before starting an update.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (queueResult.Status == RustUpdateOperationQueueStatus.OperationInProgress)
            {
                return Results.Problem(
                    title: "Operation already in progress",
                    detail: "A server-mutating update is already pending or running.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var operation = queueResult.Operation
                ?? throw new InvalidOperationException("An accepted update operation is required.");
            string location = $"/api/operations/{operation.Id}";
            return Results.Accepted(location, OperationAcceptedResponse.From(operation));
        }
        catch (RustServerControlConfigurationException)
        {
            return Results.Problem(
                title: "Rust server control is not configured",
                detail: "Rust server control configuration is incomplete or invalid.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (RustServerControlException)
        {
            return Results.Problem(
                title: "Unable to determine Rust server status",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (ProcessExecutionException)
        {
            return Results.Problem(
                title: "Unable to start Docker Compose",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .WithName("UpdateServer")
    .WithSummary("Queues a Rust server update operation.")
    .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

var updates = app.MapGroup("/api/updates");

updates.MapPost("/oxide", async (
    IOxideUpdateOperationService updateOperationService,
    CancellationToken cancellationToken) =>
    {
        try
        {
            var queueResult = await updateOperationService.QueueAsync(cancellationToken);
            if (queueResult.Status == OxideUpdateOperationQueueStatus.ServerRunning)
            {
                return Results.Problem(
                    title: "Rust server is running",
                    detail: "Stop the Rust server before starting an Oxide update.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (queueResult.Status == OxideUpdateOperationQueueStatus.OperationInProgress)
            {
                return Results.Problem(
                    title: "Operation already in progress",
                    detail: "A server-mutating update is already pending or running.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var operation = queueResult.Operation
                ?? throw new InvalidOperationException("An accepted update operation is required.");
            string location = $"/api/operations/{operation.Id}";
            return Results.Accepted(location, OperationAcceptedResponse.From(operation));
        }
        catch (RustServerControlConfigurationException)
        {
            return Results.Problem(
                title: "Rust server control is not configured",
                detail: "Rust server control configuration is incomplete or invalid.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (RustServerControlException)
        {
            return Results.Problem(
                title: "Unable to determine Rust server status",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (ProcessExecutionException)
        {
            return Results.Problem(
                title: "Unable to start Docker Compose",
                detail: "The Rust server status could not be determined.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .WithName("UpdateOxide")
    .WithSummary("Queues an Oxide update operation.")
    .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

var operations = app.MapGroup("/api/operations");

operations.MapGet("/{id:guid}", (Guid id, IOperationStore operationStore) =>
    operationStore.Get(id) is { } operation
        ? Results.Ok(OperationResponse.From(operation))
        : Results.NotFound())
    .WithName("GetOperation")
    .WithSummary("Gets the current state of an asynchronous server-mutation operation.")
    .Produces<OperationResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

static async Task<IResult> ExecuteControlOperationAsync(
    Func<CancellationToken, Task<ServerStatus>> operation,
    CancellationToken cancellationToken)
{
    try
    {
        return Results.Ok(await operation(cancellationToken));
    }
    catch (RustServerControlConfigurationException exception)
    {
        return Results.Problem(
            title: "Rust server control is not configured",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (RustServerMutationInProgressException)
    {
        return Results.Problem(
            title: "Server mutation in progress",
            detail: "A server-mutating update is pending or running.",
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (RustServerControlException exception)
    {
        return Results.Problem(
            title: "Rust server control operation failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (NetworkAccessRuleApplicationException)
    {
        return Results.Problem(
            title: "Network access rules were not applied",
            detail: "The Rust container started, but the privileged NetworkAccess helper did not apply its rules.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (ProcessExecutionException exception)
    {
        return Results.Problem(
            title: "Unable to start Docker Compose",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}

static IResult ExecuteConfigurationRead(IServerConfigurationStore configurationStore)
{
    try
    {
        return Results.Ok(ServerConfigurationResponse.From(configurationStore.Get()));
    }
    catch (ServerConfigurationStoreException)
    {
        return Results.Problem(
            title: "Server configuration is unavailable",
            detail: "Control-owned server configuration could not be read.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

static IResult ExecuteConfigurationUpdate(
    UpdateServerConfigurationRequest request,
    IServerConfigurationStore configurationStore)
{
    if (!TryCreateServerConfiguration(request, out var configuration, out var errors))
    {
        return Results.ValidationProblem(errors);
    }

    try
    {
        configurationStore.SetDesired(configuration);
        return Results.Ok(ServerConfigurationResponse.From(configurationStore.Get()));
    }
    catch (ServerConfigurationStoreException)
    {
        return Results.Problem(
            title: "Server configuration is unavailable",
            detail: "Control-owned server configuration could not be saved.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

static bool TryCreateServerConfiguration(
    UpdateServerConfigurationRequest request,
    out ServerConfiguration configuration,
    out Dictionary<string, string[]> errors)
{
    ArgumentNullException.ThrowIfNull(request);

    errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(request.Hostname))
    {
        errors["hostname"] = ["Hostname is required."];
    }
    else if (request.Hostname.Length > 200)
    {
        errors["hostname"] = ["Hostname must not exceed 200 characters."];
    }

    if (request.Description is null)
    {
        errors["description"] = ["Description is required and may be empty."];
    }
    else if (request.Description.Length > 2_000)
    {
        errors["description"] = ["Description must not exceed 2000 characters."];
    }

    if (request.MaxPlayers is < 1 or > 10_000)
    {
        errors["maxPlayers"] = ["MaxPlayers must be between 1 and 10000."];
    }

    if (request.TickRate is < 1 or > 128)
    {
        errors["tickRate"] = ["TickRate must be between 1 and 128."];
    }

    if (request.SaveInterval is < 1 or > 86_400)
    {
        errors["saveInterval"] = ["SaveInterval must be between 1 and 86400 seconds."];
    }

    if (request.Seed is < 0)
    {
        errors["seed"] = ["Seed must be between 0 and 2147483647."];
    }

    if (request.WorldSize is < 1_000 or > 6_000)
    {
        errors["worldSize"] = ["WorldSize must be between 1000 and 6000."];
    }

    if (errors.Count > 0)
    {
        configuration = null!;
        return false;
    }

    configuration = new ServerConfiguration(
        request.Hostname,
        request.Description!,
        request.MaxPlayers,
        request.TickRate,
        request.SaveInterval,
        request.Seed,
        request.WorldSize);
    return true;
}

static Task<IResult> ExecuteLifecycleMutationAsync(
    Func<CancellationToken, Task<ServerStatus>> operation,
    IOperationStore operationStore,
    CancellationToken cancellationToken)
{
    if (operationStore.HasActiveServerMutation())
    {
        return Task.FromResult<IResult>(Results.Problem(
            title: "Server mutation in progress",
            detail: "A server-mutating update is pending or running.",
            statusCode: StatusCodes.Status409Conflict));
    }

    return ExecuteControlOperationAsync(operation, cancellationToken);
}

static async Task<IResult> ExecuteStartupTrackingOperationAsync(
    Func<CancellationToken, Task<StartupSnapshot>> operation,
    CancellationToken cancellationToken)
{
    try
    {
        return Results.Ok(StartupSnapshotResponse.From(await operation(cancellationToken)));
    }
    catch (RustServerControlConfigurationException)
    {
        return Results.Problem(
            title: "Rust server control is not configured",
            detail: "Rust server control configuration is incomplete or invalid.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (StartupTrackingException)
    {
        return Results.Problem(
            title: "Unable to determine Rust server startup progress",
            detail: "The Rust server startup progress could not be determined.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (ProcessExecutionException)
    {
        return Results.Problem(
            title: "Unable to start Docker",
            detail: "The Rust server startup progress could not be determined.",
            statusCode: StatusCodes.Status502BadGateway);
    }
}

static async Task<IResult> ExecuteServerCommandAsync(
    ServerCommandRequest? request,
    IRconClient commandClient,
    CancellationToken cancellationToken)
{
    const int maximumCommandLength = 4_096;
    const int maximumResponseLength = 16_384;

    if (string.IsNullOrWhiteSpace(request?.Command))
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["command"] = ["A non-empty command is required."]
            });
    }

    if (request.Command.Length > maximumCommandLength)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["command"] = [$"Command length must not exceed {maximumCommandLength} characters."]
            });
    }

    try
    {
        var result = await commandClient.ExecuteCommandAsync(request.Command, cancellationToken);
        string responseMessage = result.ResponseMessage.Length <= maximumResponseLength
            ? result.ResponseMessage
            : result.ResponseMessage[..maximumResponseLength];

        return Results.Ok(new ServerCommandResponse(responseMessage));
    }
    catch (RconClientConfigurationException)
    {
        return Results.Problem(
            title: "Rust server RCON is not configured",
            detail: "Rust server RCON configuration is incomplete or invalid.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (RconClientException)
    {
        return Results.Problem(
            title: "Rust server command failed",
            detail: "Unable to execute the Rust server command.",
            statusCode: StatusCodes.Status502BadGateway);
    }
}

app.Run();

static string RequireIpv4BindHost(IConfiguration configuration, string key)
{
    string? value = configuration[key]?.Trim();
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
    string? value = configuration[key];
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

static Uri RequireWebSocketUrl(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || uri.Scheme != "ws"
        || !string.IsNullOrEmpty(uri.UserInfo)
        || uri.AbsolutePath != "/"
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be an absolute ws URL without credentials, a path, query, or fragment.");
    }

    return uri;
}

public partial class Program;

/// <summary>
/// RCON command submitted to the development-only server command endpoint.
/// </summary>
/// <param name="Command">Command text to execute.</param>
public sealed record ServerCommandRequest(string? Command);

/// <summary>
/// Result returned by the development-only server command endpoint.
/// </summary>
/// <param name="ResponseMessage">RCON response text, truncated to the endpoint limit when necessary.</param>
public sealed record ServerCommandResponse(string ResponseMessage);

/// <summary>
/// Desired configuration owned by Control and configuration effective for the current Rust process. The values may differ until another Rust process is successfully started.
/// </summary>
public sealed record ServerConfigurationResponse(
    ServerConfiguration Desired,
    [property: JsonRequired] ServerConfiguration? Effective)
{
    public static ServerConfigurationResponse From(ServerConfigurationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ServerConfigurationResponse(state.Desired, state.Effective);
    }
}

/// <summary>
/// Game-server settings requested for the next Rust process created by Control.
/// </summary>
public sealed record UpdateServerConfigurationRequest(
    [property: JsonRequired] string Hostname,
    [property: JsonRequired] string Description,
    [property: JsonRequired] int MaxPlayers,
    [property: JsonRequired] int TickRate,
    [property: JsonRequired] int SaveInterval,
    [property: JsonRequired] int Seed,
    [property: JsonRequired] int WorldSize);

/// <summary>
/// Explicit semantic wipe plan. It contains no filesystem paths.
/// </summary>
public sealed record WipePlanRequest(
    [property: JsonRequired] WipeAction World,
    [property: JsonRequired] WipeAction Blueprints,
    IReadOnlyList<PluginDataWipeActionRequest>? PluginData)
{
    public WipePlan ToDomain() => new(
        World,
        Blueprints,
        PluginData?.Select(action => new PluginDataWipeAction(action.Target, action.Action)).ToArray() ?? []);
}

/// <summary>
/// Requested handling of one registered plugin-data target.
/// </summary>
public sealed record PluginDataWipeActionRequest(
    [property: JsonRequired] string Target,
    [property: JsonRequired] WipeAction Action);

/// <summary>
/// Path-free result for a wipe operation.
/// </summary>
public sealed record WipeOperationResultResponse(
    bool Succeeded,
    IReadOnlyList<WipeTargetResultResponse> Targets)
{
    public static WipeOperationResultResponse From(WipeExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new WipeOperationResultResponse(
            report.Succeeded,
            report.Targets.Select(target => new WipeTargetResultResponse(
                target.Target,
                ToApiValue(target.Action),
                ToApiValue(target.Outcome),
                target.AffectedItems,
                target.Error)).ToArray());
    }

    private static string ToApiValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}

/// <summary>
/// Result for one semantic wipe target.
/// </summary>
public sealed record WipeTargetResultResponse(
    string Target,
    string Action,
    string Outcome,
    int AffectedItems,
    string? Error);

/// <summary>
/// Fixed presentation schema for Rust server startup milestones.
/// </summary>
/// <param name="Version">Schema version.</param>
/// <param name="Milestones">Ordered milestone definitions.</param>
public sealed record StartupSchemaResponse(
    int Version,
    IReadOnlyList<StartupMilestoneResponse> Milestones)
{
    public static StartupSchemaResponse From(StartupTrackingSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new StartupSchemaResponse(
            schema.Version,
            schema.Milestones
                .Select(milestone => new StartupMilestoneResponse(milestone.Id, milestone.Label))
                .ToArray());
    }
}

/// <summary>
/// Presentation information for one startup milestone.
/// </summary>
/// <param name="Id">Stable milestone identifier.</param>
/// <param name="Label">Human-readable milestone label.</param>
public sealed record StartupMilestoneResponse(string Id, string Label);

/// <summary>
/// Current startup progress for the active Rust server run.
/// </summary>
/// <param name="SessionId">Opaque startup-session identifier, or <see langword="null"/> when no Rust container is running.</param>
/// <param name="Started">Whether the final Steam server connection milestone has been observed.</param>
/// <param name="Milestones">Nine positional milestone states: pending, reached, or skipped.</param>
public sealed record StartupSnapshotResponse(
    [property: JsonRequired] string? SessionId,
    bool Started,
    IReadOnlyList<StartupMilestoneStateResponse> Milestones)
{
    public static StartupSnapshotResponse From(StartupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new StartupSnapshotResponse(
            snapshot.SessionId,
            snapshot.Started,
            snapshot.Milestones.Select(ToApiValue).ToArray());
    }

    private static StartupMilestoneStateResponse ToApiValue(StartupMilestoneState state) => state switch
    {
        StartupMilestoneState.Pending => StartupMilestoneStateResponse.Pending,
        StartupMilestoneState.Reached => StartupMilestoneStateResponse.Reached,
        StartupMilestoneState.Skipped => StartupMilestoneStateResponse.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown startup milestone state.")
    };
}

/// <summary>
/// A startup milestone's current progress state.
/// </summary>
public enum StartupMilestoneStateResponse
{
    Pending,
    Reached,
    Skipped
}

/// <summary>
/// Accepted asynchronous update operation.
/// </summary>
/// <param name="Id">Operation identifier used with <c>GET /api/operations/{id}</c>.</param>
/// <param name="Status">Initial operation status.</param>
public sealed record OperationAcceptedResponse(Guid Id, string Status)
{
    public static OperationAcceptedResponse From(Operation operation) =>
        new(operation.Id, operation.Status.ToString());
}

/// <summary>
/// Current state of an asynchronous server-mutation operation.
/// </summary>
/// <param name="Id">Operation identifier.</param>
/// <param name="Type">Operation type.</param>
/// <param name="Status">Current operation status.</param>
/// <param name="CreatedAt">UTC time at which the operation was created.</param>
/// <param name="StartedAt">UTC time at which execution started, or <see langword="null"/> while pending.</param>
/// <param name="FinishedAt">UTC time at which execution completed, or <see langword="null"/> until completion.</param>
/// <param name="ExitCode">Updater process exit code, or <see langword="null"/> until completion.</param>
/// <param name="Error">Failure message, or <see langword="null"/> when the operation has not failed.</param>
/// <param name="WipeResult">Path-free wipe target report, or <see langword="null"/> for non-wipe operations and before wipe completion.</param>
public sealed record OperationResponse(
    Guid Id,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? ExitCode,
    string? Error,
    WipeOperationResultResponse? WipeResult)
{
    public static OperationResponse From(Operation operation) =>
        new(
            operation.Id,
            operation.Type.ToString(),
            operation.Status.ToString(),
            operation.CreatedAt,
            operation.StartedAt,
            operation.FinishedAt,
            operation.ExitCode,
            operation.Error,
            operation.WipeResult is null ? null : WipeOperationResultResponse.From(operation.WipeResult));
}

internal sealed class WipeActionSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(WipeAction))
        {
            return;
        }

        if (schema is not OpenApiSchema mutableSchema)
        {
            return;
        }

        mutableSchema.Type = JsonSchemaType.String;
        mutableSchema.Format = null;
        mutableSchema.Description = "Requested handling for a known wipe target. Allowed values: `keep`, `delete`, and `transform`.";
        mutableSchema.Enum =
        [
            System.Text.Json.Nodes.JsonValue.Create("keep"),
            System.Text.Json.Nodes.JsonValue.Create("delete"),
            System.Text.Json.Nodes.JsonValue.Create("transform")
        ];
    }
}
