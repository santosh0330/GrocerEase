using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The stock of a store. Only a store user may read or change it (<c>inventory.manage</c>), <b>and</b> only in a store they run: the
/// permission says what kind of user may, the membership (checked against the database by the use case) says where, and a store the
/// caller does not run is a 404, never a 403. It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
[Route("stores/{storeId:guid}")]
public sealed class InventoryController(
    ICommandHandler<AdjustStockCommand, Result<StockResult>> adjustHandler,
    ICommandHandler<ListInventoryQuery, Result<PagedResult<InventoryEntryResult>>> listHandler,
    ICommandHandler<SetLowStockThresholdCommand, Result<InventoryEntryResult>> thresholdHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    // Every product on sale in the store with how many are on hand (zero when it has never had stock), a page at a time.
    [HttpGet("inventory")]
    [Authorize(Policy = Permissions.InventoryManage)]
    [ProducesResponseType<PagedResponse<InventoryEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> List(
        Guid storeId, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await listHandler.Handle(new ListInventoryQuery(storeId, callerId, page, pageSize), cancellationToken);
        return result.Match<IActionResult>(entries => Ok(entries.ToResponse()), error => this.ToProblem(error));
    }

    // The change is an amount: positive adds, negative removes. Never "set the quantity to": that would overwrite whatever arrived meanwhile.
    [HttpPost("products/{productId:guid}/stock-adjustments")]
    [Authorize(Policy = Permissions.InventoryManage)]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Adjust(Guid storeId, Guid productId, AdjustStockRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await adjustHandler.Handle(request.ToCommand(storeId, productId, callerId), cancellationToken);
        return result.Match<IActionResult>(stock => Ok(stock.ToResponse()), error => this.ToProblem(error));
    }

    // The level at or below which the product counts as low: { "threshold": 5 }, or null to clear it. It is a setting, not an amount, so
    // this is a PUT: sending the same body twice leaves the same level. A product that has never had stock gets its row, at zero.
    [HttpPut("products/{productId:guid}/low-stock-threshold")]
    [Authorize(Policy = Permissions.InventoryManage)]
    [ProducesResponseType<InventoryEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> SetLowStockThreshold(Guid storeId, Guid productId, SetLowStockThresholdRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await thresholdHandler.Handle(request.ToCommand(storeId, productId, callerId), cancellationToken);
        return result.Match<IActionResult>(entry => Ok(entry.ToResponse()), error => this.ToProblem(error));
    }
}
