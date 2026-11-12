using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Common;

internal static class StoreResultMapping
{
    public static StoreResult ToResult(this Store store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new StoreResult(store.Id, store.Name, store.Address);
    }
}
