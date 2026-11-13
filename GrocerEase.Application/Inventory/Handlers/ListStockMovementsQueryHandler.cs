using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Inventory.Handlers;

/// <summary>
/// Reads the history of a product's stock for the people who run the store: every change to its quantity, newest first, with by how much,
/// why, who did it and which order it was for. Like the stock itself, the history is the store's own business, so a store the caller does not
/// run is the same <c>Store.NotFound</c> as one that does not exist, and a product that is not on sale in the store is <c>Product.NotFound</c>.
/// </summary>
public sealed class ListStockMovementsQueryHandler(StoreAccess storeAccess, IProductRepository productRepository, IInventoryItemRepository inventoryRepository)
    : ICommandHandler<ListStockMovementsQuery, Result<PagedResult<StockMovementResult>>>
{
    public async Task<Result<PagedResult<StockMovementResult>>> Handle(ListStockMovementsQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<PagedResult<StockMovementResult>>(notAMember);
        }

        // An unknown product, an archived one and another store's are all "not found", so a history is only ever asked for in its own store.
        if (await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken) is null)
        {
            return Result.Failure<PagedResult<StockMovementResult>>(ProductErrors.NotFound);
        }

        var page = await inventoryRepository.ListMovementsAsync(command.StoreId, command.ProductId, command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(movement =>
            new StockMovementResult(movement.Id, movement.ProductId, movement.Delta, movement.Reason.ToString(), movement.ActorId, movement.OrderId, movement.CreatedAt)));
    }
}
