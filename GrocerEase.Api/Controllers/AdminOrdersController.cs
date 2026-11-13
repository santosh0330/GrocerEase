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
/// The orders of every store, for the admin, <b>read only</b>. It has its own route and its own permission (<c>orders.read.all</c>) instead
/// of a second policy on <c>/stores/{storeId}/orders</c>: two policies on one action must both pass, so they would lock out the very people
/// each one is meant to let in. There is no verify, ship or cancel here: the admin watches, a store user acts. It holds no logic: it runs the
/// handler and maps the result.
/// </summary>
[ApiController]
[Route("admin/orders")]
public sealed class AdminOrdersController(ICommandHandler<ListAllOrdersQuery, Result<PagedResult<OrderResult>>> listHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    // Newest first, so the latest activity is on top. ?storeId= narrows it to one store and ?status= to one status; a store that does not exist is an empty page.
    [HttpGet]
    [Authorize(Policy = Permissions.OrdersReadAll)]
    [ProducesResponseType<PagedResponse<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken, [FromQuery] Guid? storeId = null, [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        var result = await listHandler.Handle(new ListAllOrdersQuery(storeId, status, page, pageSize), cancellationToken);
        return result.Match<IActionResult>(orders => Ok(orders.ToResponse()), error => this.ToProblem(error));
    }
}
