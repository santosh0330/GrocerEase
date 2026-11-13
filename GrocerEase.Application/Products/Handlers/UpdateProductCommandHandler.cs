using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;

namespace GrocerEase.Application.Products.Handlers;

/// <summary>
/// Changes a product's name, description and price, <b>on top of the version the person read</b>. Two people editing the same product do not
/// overwrite each other: the first to save wins, and the second is told the product has changed and shown how it is now, instead of
/// silently replacing what the first did. Nothing is locked while anyone edits (that is optimistic concurrency): the check is the write.
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

        // The quick answer: the product is already at another version than the one the person read. The write below would refuse it too, and
        // is the one that decides when two edits arrive together; this only saves it a statement when the answer is already known.
        if (current.Version != command.Version)
        {
            return Result.Failure<ProductResult>(ProductErrors.Outdated(current.ToResult()));
        }

        var edited = current.WithDetails(command.Name, command.Description, command.Price);
        return await productRepository.TryUpdateAsync(edited, command.Version, cancellationToken) switch
        {
            ProductUpdateOutcome.Updated => Result.Success(edited.ToResult()),
            ProductUpdateOutcome.DuplicateName => Result.Failure<ProductResult>(ProductErrors.DuplicateName),
            ProductUpdateOutcome.Outdated => await OutdatedAsync(command, cancellationToken),
            _ => Result.Failure<ProductResult>(ProductErrors.NotFound),
        };
    }

    // Someone else's edit won the write. The answer carries the product as it is now (read again: it is theirs, not the one this request read).
    private async Task<Result<ProductResult>> OutdatedAsync(UpdateProductCommand command, CancellationToken cancellationToken) =>
        await productRepository.GetLiveAsync(command.StoreId, command.ProductId, cancellationToken) is { } now
            ? Result.Failure<ProductResult>(ProductErrors.Outdated(now.ToResult()))
            : Result.Failure<ProductResult>(ProductErrors.NotFound); // archived in the meantime: there is nothing left to edit
}
