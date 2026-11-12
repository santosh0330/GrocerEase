using GrocerEase.Application.Common;

namespace GrocerEase.Application.Stores.Common;

/// <summary>The expected failures of the store use cases. Codes are stable: clients may switch on them.</summary>
public static class StoreErrors
{
    public static readonly ResultError DuplicateName = new(
        "Store.DuplicateName", "A store with that name already exists.", ErrorType.Conflict);

    public static readonly ResultError NotFound = new(
        "Store.NotFound", "There is no store with that id.", ErrorType.NotFound);
}
