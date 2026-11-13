using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Products.Handlers;

/// <summary>
/// "Deletes" a product by archiving it: it leaves the catalogue and frees its name, but the row stays, so a past order can still say what
/// was bought. Returns the product that was archived.
/// </summary>
public sealed class ArchiveProductCommandHandler(StoreAccess storeAccess, IProductRepository productRepository, TimeProvider timeProvider)
    : ICommandHandler<ArchiveProductCommand, Result<ProductResult>>
{
    public async Task<Result<ProductResult>> Handle(ArchiveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<ProductResult>(notAMember);
        }

        // Read for the answer, and for a clear NotFound. The statement below is what decides: only one of two requests archiving the same
        // product finds it still on sale, and the other is told it is gone.
        var product = await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken);
        if (product is null || !await productRepository.TryArchiveAsync(command.StoreId, command.ProductId, timeProvider.GetUtcNow(), cancellationToken))
        {
            return Result.Failure<ProductResult>(ProductErrors.NotFound);
        }

        return Result.Success(product.ToResult());
    }
}
