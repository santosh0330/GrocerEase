using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Inventory.Handlers;

/// <summary>
/// Lists the products on sale in a store with how many of each are on hand. Only the people who run the store may see it: unlike the
/// catalogue, stock is the store's own business. It asks for a page of products, then for the quantities of exactly those, each from its
/// own repository, so each table is read in one place and always scoped by store.
/// </summary>
public sealed class ListInventoryQueryHandler(
    StoreAccess storeAccess, IProductRepository productRepository, IInventoryItemRepository inventoryRepository, ILowStockRepository lowStockRepository)
    : ICommandHandler<ListInventoryQuery, Result<PagedResult<InventoryEntryResult>>>
{
    public async Task<Result<PagedResult<InventoryEntryResult>>> Handle(ListInventoryQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<PagedResult<InventoryEntryResult>>(notAMember);
        }

        // Only what is running out: a different question, answered by the read-side repository that can look at products and stock together.
        if (command.LowStockOnly)
        {
            return Result.Success(await lowStockRepository.ListLowAsync(command.StoreId, command.Page, command.PageSize, cancellationToken));
        }

        var products = await productRepository.ListLiveAsync(command.StoreId, command.Page, command.PageSize, cancellationToken);
        var items = await inventoryRepository.GetItemsAsync(command.StoreId, [.. products.Items.Select(product => product.Id)], cancellationToken);

        // A product with no stock row has none on hand, and no level.
        return Result.Success(products.Map(product => items.TryGetValue(product.Id, out var item)
            ? InventoryEntryResult.FromItem(product.Name, item)
            : InventoryEntryResult.WithNoStock(product.Id, product.Name)));
    }
}
