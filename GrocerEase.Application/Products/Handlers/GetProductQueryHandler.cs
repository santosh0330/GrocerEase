using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Queries;

namespace GrocerEase.Application.Products.Handlers;

/// <summary>Reads one product that is on sale. Anyone with <c>products.read</c> may: browsing is for customers, so there is no ownership rule here.</summary>
public sealed class GetProductQueryHandler(IProductRepository productRepository) : ICommandHandler<GetProductQuery, Result<ProductResult>>
{
    public async Task<Result<ProductResult>> Handle(GetProductQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken);
        return product is null ? Result.Failure<ProductResult>(ProductErrors.NotFound) : Result.Success(product.ToResult());
    }
}
