using GrocerEase.Api.Mapping;
using GrocerEase.Api.RateLimiting;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// A customer's orders. Only a customer orders (<c>orders.place</c>) and reads their own orders (<c>orders.read</c>); a store user or an
/// admin gets a 403. Who the customer is comes from the verified token and is put in the command by this controller, never read from the
/// body, so an order cannot be placed or read in someone else's name. Someone else's order is a 404, the same as one that does not exist.
/// It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
public sealed class OrdersController(
    ICommandHandler<PlaceOrderCommand, Result<OrderResult>> placeHandler,
    ICommandHandler<GetMyOrderQuery, Result<OrderResult>> getHandler,
    ICommandHandler<ListMyOrdersQuery, Result<PagedResult<OrderResult>>> listHandler,
    ICommandHandler<CancelMyOrderCommand, Result<OrderResult>> cancelHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    // The route says which store the order is for, like the other store-owned routes; the order itself is then read at /orders/{id}.
    [HttpPost("/stores/{storeId:guid}/orders")]
    [Authorize(Policy = Permissions.OrdersPlace)]
    [EnableRateLimiting(RateLimitingExtensions.PlaceOrderPolicy)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemJson)]
    public async Task<IActionResult> Place(Guid storeId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } customerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await placeHandler.Handle(request.ToCommand(storeId, customerId), cancellationToken);
        return result.Match<IActionResult>(
            order => CreatedAtAction(nameof(Get), new { orderId = order.Id }, order.ToResponse()),
            error => this.ToProblem(error));
    }

    // The caller's own orders, newest first. There is no way to ask for someone else's: the customer is the one in the token.
    [HttpGet("/orders")]
    [Authorize(Policy = Permissions.OrdersRead)]
    [ProducesResponseType<PagedResponse<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        if (User.GetUserId() is not { } customerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await listHandler.Handle(new ListMyOrdersQuery(customerId, page, pageSize), cancellationToken);
        return result.Match<IActionResult>(orders => Ok(orders.ToResponse()), error => this.ToProblem(error));
    }

    [HttpGet("/orders/{orderId:guid}")]
    [Authorize(Policy = Permissions.OrdersRead)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } customerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await getHandler.Handle(new GetMyOrderQuery(customerId, orderId), cancellationToken);
        return result.Match<IActionResult>(order => Ok(order.ToResponse()), error => this.ToProblem(error));
    }

    // A customer takes back their own order while the store has not looked at it (Pending), and the stock goes back. Once it is verified,
    // only the store can cancel it (409). Someone else's order is a 404, the same as one that does not exist.
    [HttpPost("/orders/{orderId:guid}/cancel")]
    [Authorize(Policy = Permissions.OrdersCancel)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } customerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await cancelHandler.Handle(new CancelMyOrderCommand(customerId, orderId), cancellationToken);
        return result.Match<IActionResult>(order => Ok(order.ToResponse()), error => this.ToProblem(error));
    }
}
