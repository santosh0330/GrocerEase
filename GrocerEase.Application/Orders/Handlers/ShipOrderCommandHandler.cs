using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// The store sends a verified order: final, and nothing about its stock changes (it left when the order was placed). Only the status
/// changes, in one statement, so it needs no unit of work.
/// </summary>
public sealed class ShipOrderCommandHandler(StoreAccess storeAccess, IOrderRepository orderRepository)
    : ICommandHandler<ShipOrderCommand, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var moved = await OrderMoves.MoveAsync(
            storeAccess, orderRepository, command.StoreId, command.OrderId, command.ActorId, "shipped", order => order.Ship(), cancellationToken);
        return moved.IsSuccess ? Result.Success(moved.Value.ToResult()) : Result.Failure<OrderResult>(moved.Error);
    }
}
