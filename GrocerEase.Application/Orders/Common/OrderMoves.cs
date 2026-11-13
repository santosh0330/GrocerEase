using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Common;

/// <summary>
/// What verifying, shipping and cancelling an order have in common: the caller runs the store, the order is in that store, the entity says
/// the move is legal, and the database changes the status only if nobody changed it first. The entity owns <i>whether</i> a move is
/// legal; the database owns <i>who wins</i> when two callers try at once.
/// </summary>
internal static class OrderMoves
{
    /// <summary>
    /// Returns the order after the move, or the error to answer with. <paramref name="move"/> is the entity's own method
    /// (<see cref="Order.Verify"/>); <paramref name="pastTense"/> names it in the message ("verified").
    /// </summary>
    public static async Task<Result<Order>> MoveAsync(
        StoreAccess storeAccess,
        IOrderRepository orderRepository,
        Guid storeId,
        Guid orderId,
        Guid actorId,
        string pastTense,
        Func<Order, bool> move,
        CancellationToken cancellationToken)
    {
        if (await storeAccess.RequireMemberAsync(storeId, actorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<Order>(notAMember);
        }

        // Read through the store: an order of another store is "not found", the same as one that does not exist.
        if (await orderRepository.GetForStoreAsync(storeId, orderId, cancellationToken) is not { } order)
        {
            return Result.Failure<Order>(OrderErrors.NotFound);
        }

        var from = order.Status;
        if (!move(order))
        {
            return Result.Failure<Order>(OrderErrors.InvalidTransition(pastTense, from));
        }

        // The status the order had when it was read is the one the row must still have.
        return await orderRepository.TryChangeStatusAsync(storeId, orderId, from, order.Status, cancellationToken)
            ? Result.Success(order)
            : Result.Failure<Order>(OrderErrors.ChangedMeanwhile);
    }
}
