using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Products.Handlers;

public sealed class CreateProductCommandHandler(StoreAccess storeAccess, IProductRepository productRepository)
    : ICommandHandler<CreateProductCommand, Result<ProductResult>>
{
    public async Task<Result<ProductResult>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The ownership rule first: a user who does not run the store learns nothing about it, not even that it exists.
        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<ProductResult>(notAMember);
        }

        var product = new Product(Guid.CreateVersion7(), command.StoreId, command.Name, command.Description, command.Price);

        // No check first: the partial unique index decides, and a check followed by an insert could be overtaken anyway.
        return await productRepository.TryAddAsync(product, cancellationToken)
            ? Result.Success(product.ToResult())
            : Result.Failure<ProductResult>(ProductErrors.DuplicateName);
    }
}
