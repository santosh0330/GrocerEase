using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Products.Handlers;

/// <summary>Lists the products on sale in a store. Anyone with <c>products.read</c> may: browsing is for customers, so there is no ownership rule here, only a store that must exist.</summary>
public sealed class ListProductsQueryHandler(IStoreRepository storeRepository, IProductRepository productRepository)
    : ICommandHandler<ListProductsQuery, Result<PagedResult<ProductResult>>>
{
    public async Task<Result<PagedResult<ProductResult>>> Handle(ListProductsQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeRepository.GetByIdAsync(command.StoreId, cancellationToken) is null)
        {
            return Result.Failure<PagedResult<ProductResult>>(StoreErrors.NotFound);
        }

        var page = await productRepository.ListLiveAsync(command.StoreId, command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(product => product.ToResult()));
    }
}
