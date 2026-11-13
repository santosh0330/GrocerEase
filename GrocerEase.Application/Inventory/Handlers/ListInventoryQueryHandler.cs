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
public sealed class ListInventoryQueryHandler(StoreAccess storeAccess, IProductRepository productRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<ListInventoryQuery, Result<PagedResult<InventoryEntryResult>>>
{
    public async Task<Result<PagedResult<InventoryEntryResult>>> Handle(ListInventoryQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<PagedResult<InventoryEntryResult>>(notAMember);
        }

        var products = await productRepository.ListLiveAsync(command.StoreId, command.Page, command.PageSize, cancellationToken);
        var quantities = await inventoryRepository.GetQuantitiesAsync(command.StoreId, [.. products.Items.Select(product => product.Id)], cancellationToken);

        // A product with no stock row has none on hand.
        return Result.Success(products.Map(product =>
            new InventoryEntryResult(product.Id, product.Name, quantities.TryGetValue(product.Id, out var quantity) ? quantity : 0)));
    }
}
