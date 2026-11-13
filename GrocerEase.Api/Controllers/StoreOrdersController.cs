using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The orders of a store, seen and moved along by the people who run it. Only a store user may (<c>orders.manage</c>), <b>and</b> only in a
/// store they run: the permission says what kind of user, the membership (checked against the database by the use case) says where, and
/// a store the caller does not run is a 404, never a 403. It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
[Route("stores/{storeId:guid}/orders")]
public sealed class StoreOrdersController(
    ICommandHandler<ListStoreOrdersQuery, Result<PagedResult<OrderResult>>> listHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    // Oldest first, so the one that has waited longest is on top. ?status=Pending is the queue a store user works through.
    [HttpGet]
    [Authorize(Policy = Permissions.OrdersManage)]
    [ProducesResponseType<PagedResponse<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> List(
        Guid storeId, CancellationToken cancellationToken, [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await listHandler.Handle(new ListStoreOrdersQuery(storeId, callerId, status, page, pageSize), cancellationToken);
        return result.Match<IActionResult>(orders => Ok(orders.ToResponse()), error => this.ToProblem(error));
    }
}
