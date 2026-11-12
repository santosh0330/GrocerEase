using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;

namespace GrocerEase.Application.Stores.Handlers;

public sealed class GetStoreQueryHandler(IStoreRepository storeRepository) : ICommandHandler<GetStoreQuery, Result<StoreResult>>
{
    public async Task<Result<StoreResult>> Handle(GetStoreQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var store = await storeRepository.GetByIdAsync(command.StoreId, cancellationToken);
        return store is null ? Result.Failure<StoreResult>(StoreErrors.NotFound) : Result.Success(store.ToResult());
    }
}
