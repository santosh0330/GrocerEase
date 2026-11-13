using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// A customer who changed their mind cancels their own order, <b>while it is still pending</b>, and the stock it reserved goes back. Once
/// the store has verified it, only the store can cancel. It is a transactional handler, like the store's cancellation, and it shares what
/// matters with it: the status changes first, by a compare-and-set, and only the winner restores the stock.
/// </summary>
/// <remarks>
/// <b>Why the guard is the same statement.</b> A customer withdrawing an order while a store user verifies it is a race with exactly one
/// winner: the status change is <c>UPDATE ... WHERE status = 'Pending'</c>, so if the verification got there first the withdrawal matches
/// nothing and is refused, and if the withdrawal got there first the verification is refused. A check ("is it still pending?") followed
/// by a write would let both succeed, and the stock of an order that is now verified would go back to the shelf.
/// </remarks>
public sealed class CancelMyOrderCommandHandler(IOrderRepository orderRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<CancelMyOrderCommand, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(CancelMyOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Read through the customer: someone else's order is "not found", the same as one that does not exist.
        var moved = await OrderMoves.MoveMineAsync(
            orderRepository, command.CustomerId, command.OrderId, "cancelled", order => order.Withdraw(), cancellationToken);
        if (moved.IsFailure)
        {
            return Result.Failure<OrderResult>(moved.Error);
        }

        var order = moved.Value;
        return await OrderStock.RestoreAsync(inventoryRepository, order, command.CustomerId, cancellationToken) is { } cannotRestore
            ? Result.Failure<OrderResult>(cannotRestore) // the decorator rolls back the status change and every line already restored
            : Result.Success(order.ToResult());
    }
}
