using GrocerEase.Application.Common;

namespace GrocerEase.Application.Stores.Common;

/// <summary>The expected failures of the store use cases. Codes are stable: clients may switch on them.</summary>
public static class StoreErrors
{
    public static readonly ResultError DuplicateName = new(
        "Store.DuplicateName", "A store with that name already exists.", ErrorType.Conflict);

    public static readonly ResultError NotFound = new(
        "Store.NotFound", "There is no store with that id.", ErrorType.NotFound);

    // An admin may learn whether an email is registered: this route is theirs alone (stores.manage).
    public static readonly ResultError UserNotFound = new(
        "User.NotFound", "There is no user with that email.", ErrorType.NotFound);

    // One error for an admin, a store user and a customer another request has just assigned, so the caller learns only what they can act on.
    public static readonly ResultError UserNotACustomer = new(
        "User.NotACustomer", "Only a customer can be assigned to a store.", ErrorType.Conflict);
}
