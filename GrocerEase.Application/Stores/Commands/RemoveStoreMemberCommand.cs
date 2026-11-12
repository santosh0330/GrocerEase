namespace GrocerEase.Application.Stores.Commands;

/// <summary>Intent to take a user out of a store, which makes them a customer again. Application's own type: it must not reference Contracts.</summary>
public sealed record RemoveStoreMemberCommand(Guid StoreId, Guid UserId);
