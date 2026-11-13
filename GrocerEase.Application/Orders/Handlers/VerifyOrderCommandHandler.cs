using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// The store accepts a pending order. Only the status changes, in one statement, so it needs no unit of work: the stock was reserved
/// when the order was placed.
/// </summary>
public sealed class VerifyOrderCommandHandler(StoreAccess storeAccess, IOrderRepository orderRepository)
    : ICommandHandler<VerifyOrderCommand, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(VerifyOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var moved = await OrderMoves.MoveAsync(
            storeAccess, orderRepository, command.StoreId, command.OrderId, command.ActorId, "verified", order => order.Verify(), cancellationToken);
        return moved.IsSuccess ? Result.Success(moved.Value.ToResult()) : Result.Failure<OrderResult>(moved.Error);
    }
}
