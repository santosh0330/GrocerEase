using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;

namespace GrocerEase.Application.Stores.Handlers;

/// <summary>Lists every store. It reuses <see cref="ICommandHandler{TCommand, TResponse}"/> for a read, as <c>ListRolesQueryHandler</c> does.</summary>
public sealed class ListStoresQueryHandler(IStoreRepository storeRepository)
    : ICommandHandler<ListStoresQuery, Result<IReadOnlyList<StoreResult>>>
{
    public async Task<Result<IReadOnlyList<StoreResult>>> Handle(ListStoresQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stores = await storeRepository.ListAsync(cancellationToken);
        IReadOnlyList<StoreResult> results = [.. stores.Select(store => store.ToResult())];
        return Result.Success(results);
    }
}
