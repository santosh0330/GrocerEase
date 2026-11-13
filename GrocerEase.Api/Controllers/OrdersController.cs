using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Contracts.Orders;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    ICommandHandler<GetMyOrderQuery, Result<OrderResult>> getHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    // The route says which store the order is for, like the other store-owned routes; the order itself is then read at /orders/{id}.
    [HttpPost("/stores/{storeId:guid}/orders")]
    [Authorize(Policy = Permissions.OrdersPlace)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
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
}
