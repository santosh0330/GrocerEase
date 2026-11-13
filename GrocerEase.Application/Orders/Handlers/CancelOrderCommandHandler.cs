using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// The store cancels an order that has not shipped, and the stock the order reserved goes back. It is registered as a transactional handler:
/// the status change and every line's restored stock are kept together or not at all, so an order is never cancelled with its stock still
/// taken, and stock is never put back for an order that is still alive.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status is changed first, and only the winner restores.</b> The move is a compare-and-set, so when two store users cancel the
/// same order at once, exactly one statement changes the row; the other finds the order already cancelled, returns before it touches
/// any stock, and the quantity goes back once, not twice. The same holds against a shipment: an order that was shipped first cannot be
/// cancelled, so its stock is not returned for goods that left.
/// </para>
/// <para>
/// <b>The lines are restored in order of product id</b>, like they are reserved when an order is placed: a stock row stays locked until the
/// unit ends, and two cancellations that share products and take them in opposite orders would wait for each other. (Placing an order
/// locks stock rows but no existing order row, so a cancellation, which locks its order row first, cannot be one end of that cycle.)
/// </para>
/// </remarks>
public sealed class CancelOrderCommandHandler(StoreAccess storeAccess, IOrderRepository orderRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<CancelOrderCommand, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var moved = await OrderMoves.MoveAsync(
            storeAccess, orderRepository, command.StoreId, command.OrderId, command.ActorId, "cancelled", order => order.Cancel(), cancellationToken);
        if (moved.IsFailure)
        {
            return Result.Failure<OrderResult>(moved.Error);
        }

        var order = moved.Value;
        return await OrderStock.RestoreAsync(inventoryRepository, order, cancellationToken) is { } cannotRestore
            ? Result.Failure<OrderResult>(cannotRestore) // the decorator rolls back the status change and every line already restored
            : Result.Success(order.ToResult());
    }
}
