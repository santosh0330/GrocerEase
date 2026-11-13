using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Products.Handlers;

/// <summary>
/// Changes a product's name, description and price. Two people editing the same product at the same moment do not know about each other:
/// the last write wins, which a version on the product will fix (a later slice).
/// </summary>
public sealed class UpdateProductCommandHandler(StoreAccess storeAccess, IProductRepository productRepository)
    : ICommandHandler<UpdateProductCommand, Result<ProductResult>>
{
    public async Task<Result<ProductResult>> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<ProductResult>(notAMember);
        }

        // Read for the id and the store, so the edited copy keeps them. The write below names both again and only touches a product that
        // is still on sale: a product archived in between is not brought back by an edit.
        var current = await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken);
        if (current is null)
        {
            return Result.Failure<ProductResult>(ProductErrors.NotFound);
        }

        var edited = current.WithDetails(command.Name, command.Description, command.Price);
        return await productRepository.TryUpdateAsync(edited, cancellationToken) switch
        {
            ProductUpdateOutcome.Updated => Result.Success(edited.ToResult()),
            ProductUpdateOutcome.DuplicateName => Result.Failure<ProductResult>(ProductErrors.DuplicateName),
            _ => Result.Failure<ProductResult>(ProductErrors.NotFound),
        };
    }
}
