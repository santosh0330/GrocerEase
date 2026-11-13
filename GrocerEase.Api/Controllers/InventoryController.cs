using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
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
public sealed class InventoryController(ICommandHandler<AdjustStockCommand, Result<StockResult>> adjustHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

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
}
