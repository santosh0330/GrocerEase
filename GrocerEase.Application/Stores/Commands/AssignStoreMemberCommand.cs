namespace GrocerEase.Application.Stores.Commands;

/// <summary>Intent to assign the customer with this email to a store, which makes them a store user. Application's own type: it must not reference Contracts.</summary>
public sealed record AssignStoreMemberCommand(Guid StoreId, string Email);
