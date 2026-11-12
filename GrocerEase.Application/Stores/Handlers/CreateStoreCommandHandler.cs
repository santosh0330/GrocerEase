using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Handlers;

public sealed class CreateStoreCommandHandler(IStoreRepository storeRepository) : ICommandHandler<CreateStoreCommand, Result<StoreResult>>
{
    public async Task<Result<StoreResult>> Handle(CreateStoreCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var store = new Store(Guid.CreateVersion7(), command.Name, command.Address);

        // No check first: the unique index decides, and a check followed by an insert could be overtaken by another request anyway.
        return await storeRepository.TryAddAsync(store, cancellationToken)
            ? Result.Success(store.ToResult())
            : Result.Failure<StoreResult>(StoreErrors.DuplicateName);
    }
}
