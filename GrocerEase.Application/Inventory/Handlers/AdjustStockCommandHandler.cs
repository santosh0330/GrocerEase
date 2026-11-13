using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Handlers;

/// <summary>
/// Adds or removes stock. A change is an <b>amount</b>, not a new total: "add 12" is not overwritten by the order that arrived while the
/// person was counting, and "set to 40" would be.
/// </summary>
public sealed class AdjustStockCommandHandler(
    StoreAccess storeAccess, IProductRepository productRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<AdjustStockCommand, Result<StockResult>>
{
    public async Task<Result<StockResult>> Handle(AdjustStockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<StockResult>(notAMember);
        }

        // Only a product that is on sale in this store has stock to change.
        if (await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken) is null)
        {
            return Result.Failure<StockResult>(ProductErrors.NotFound);
        }

        // The statement decides, with the limits inside it. A refusal can only mean the quantity would leave its range, and the sign of the
        // change says which end.
        // Said by hand: a delivery adds, anything that takes stock off the shelf by hand is a correction. (A later change lets the person say which.)
        var reason = command.Delta > 0 ? StockMovementReason.Received : StockMovementReason.Correction;
        var change = new StockChange(command.Delta, reason, command.ActorId);

        return await inventoryRepository.TryAdjustAsync(command.StoreId, command.ProductId, change, cancellationToken) is { } quantity
            ? Result.Success(new StockResult(command.ProductId, quantity))
            : Result.Failure<StockResult>(command.Delta < 0 ? InventoryErrors.NotEnoughStock : InventoryErrors.TooMuchStock);
    }
}
