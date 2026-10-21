using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Exposes durable operation state and history.
/// </summary>
[ApiController]
[Route("api/operations")]
[Produces("application/json")]
public sealed class OperationsController(
    GetOperationHandler getOperationHandler,
    GetOperationsHandler getOperationsHandler) : ControllerBase
{
    /// <summary>
    /// Gets one operation by identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var operation = await getOperationHandler.HandleAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return operation is null ? NotFound() : Ok(operation);
    }

    /// <summary>
    /// Gets the newest operation records.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<OperationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyCollection<OperationResponse>>> GetAll(
        [FromQuery, Range(1, GetOperationsHandler.MaximumLimit)] int limit = GetOperationsHandler.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await getOperationsHandler.HandleAsync(limit, cancellationToken));
    }

}
