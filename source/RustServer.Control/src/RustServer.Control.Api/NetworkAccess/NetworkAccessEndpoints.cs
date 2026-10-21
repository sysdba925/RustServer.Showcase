using System.Text.Json.Serialization;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Api.NetworkAccess;

public static class NetworkAccessEndpoints
{
    public static RouteGroupBuilder MapNetworkAccessEndpoints(this RouteGroupBuilder server)
    {
        server.MapGet("/network-access", (GetNetworkAccessQueryHandler queryHandler) =>
                ExecuteGet(queryHandler))
            .WithName("GetNetworkAccess")
            .WithSummary("Gets the persisted public reachability mode and maintenance UDP port.")
            .Produces<NetworkAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        server.MapPut("/network-access", async (
                UpdateNetworkAccessRequest request,
                UpdateNetworkAccessCommandHandler commandHandler,
                CancellationToken cancellationToken) =>
                await ExecuteUpdateAsync(request, commandHandler, cancellationToken))
            .WithName("UpdateNetworkAccess")
            .WithSummary("Applies Docker-aware nftables rules without restarting the Rust server or recreating its container.")
            .Produces<NetworkAccessResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return server;
    }

    private static IResult ExecuteGet(GetNetworkAccessQueryHandler queryHandler)
    {
        try
        {
            return Results.Ok(NetworkAccessResponse.From(queryHandler.Handle()));
        }
        catch (NetworkAccessSettingsStoreException)
        {
            return Results.Problem(
                title: "Network access settings are unavailable",
                detail: "Control-owned NetworkAccess settings could not be read.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ExecuteUpdateAsync(
        UpdateNetworkAccessRequest request,
        UpdateNetworkAccessCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await commandHandler.HandleAsync(
                new UpdateNetworkAccessCommand(request.PublicEnabled, request.MaintenancePort),
                cancellationToken);
            return Results.Ok(NetworkAccessResponse.From(settings));
        }
        catch (NetworkAccessValidationException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [exception.Field] = [exception.Message]
                });
        }
        catch (NetworkAccessRuleApplicationException exception)
            when (exception.FailureKind == NetworkAccessRuleFailureKind.PortInUse)
        {
            return Results.Problem(
                title: "Maintenance port is unavailable",
                detail: "The requested maintenance UDP port is already in use.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (NetworkAccessRuleApplicationException exception)
        {
            var detail = exception.FailureKind switch
            {
                NetworkAccessRuleFailureKind.HelperUnavailable =>
                    "The privileged NetworkAccess helper is unavailable. Check its systemd service.",
                NetworkAccessRuleFailureKind.ContainerUnavailable =>
                    "The Rust container is unavailable. The public ports remain blocked.",
                NetworkAccessRuleFailureKind.UnsupportedDockerFirewall =>
                    "Docker's DOCKER-USER firewall chain is unavailable.",
                NetworkAccessRuleFailureKind.ApplyFailed =>
                    "The firewall could not apply the requested rules. Check the helper journal.",
                _ => "The NetworkAccess helper rejected the requested firewall configuration."
            };
            return Results.Problem(
                title: "Network access rules were not applied",
                detail: detail,
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["code"] = exception.FailureKind.ToString() });
        }
        catch (NetworkAccessSettingsStoreException)
        {
            return Results.Problem(
                title: "Network access settings are unavailable",
                detail: "Control-owned NetworkAccess settings could not be saved.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>
/// Network reachability requested for the Rust Dedicated Server.
/// </summary>
public sealed record UpdateNetworkAccessRequest(
    [property: JsonRequired] bool PublicEnabled,
    [property: JsonRequired] int MaintenancePort);

/// <summary>
/// Persisted NetworkAccess mode and UDP maintenance entry point.
/// </summary>
public sealed record NetworkAccessResponse(bool PublicEnabled, int MaintenancePort)
{
    public static NetworkAccessResponse From(NetworkAccessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new NetworkAccessResponse(settings.PublicEnabled, settings.MaintenancePort);
    }
}
