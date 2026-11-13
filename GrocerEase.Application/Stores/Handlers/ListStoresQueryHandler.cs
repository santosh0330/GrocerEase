using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;

namespace GrocerEase.Application.Stores.Handlers;

/// <summary>Lists the stores, a page at a time. It reuses <see cref="ICommandHandler{TCommand, TResponse}"/> for a read, as <c>ListRolesQueryHandler</c> does.</summary>
public sealed class ListStoresQueryHandler(IStoreRepository storeRepository)
    : ICommandHandler<ListStoresQuery, Result<PagedResult<StoreResult>>>
{
    public async Task<Result<PagedResult<StoreResult>>> Handle(ListStoresQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var page = await storeRepository.ListAsync(command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(store => store.ToResult()));
    }
}
