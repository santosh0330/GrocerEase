using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Handlers;

/// <summary>
/// Sets the level at which the store wants to be told a product is running out. The level lives on the product's stock row, which exists
/// only after a first adjustment, so for a product that has never had stock the call makes the row, at zero (the same upsert the first
/// adjustment uses). Only the level is written, never the quantity, so setting it cannot undo a sale that happens at the same moment.
/// </summary>
public sealed class SetLowStockThresholdCommandHandler(StoreAccess storeAccess, IProductRepository productRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<SetLowStockThresholdCommand, Result<InventoryEntryResult>>
{
    public async Task<Result<InventoryEntryResult>> Handle(SetLowStockThresholdCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<InventoryEntryResult>(notAMember);
        }

        // Only a product that is on sale in this store has a level to set (an archived one, an unknown one and another store's are "not found").
        if (await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken) is not { } product)
        {
            return Result.Failure<InventoryEntryResult>(ProductErrors.NotFound);
        }

        // The statement returns the quantity; the row it describes is the quantity and the level just set, so the entity says whether it is low.
        return await inventoryRepository.SetLowStockThresholdAsync(command.StoreId, command.ProductId, command.Threshold, cancellationToken) is { } quantity
            ? Result.Success(InventoryEntryResult.FromItem(product.Name, new InventoryItem(product.Id, command.StoreId, quantity, command.Threshold)))
            : Result.Failure<InventoryEntryResult>(ProductErrors.NotFound); // archived in the instant between: the same answer
    }
}
