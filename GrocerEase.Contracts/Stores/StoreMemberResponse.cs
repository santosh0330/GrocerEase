namespace GrocerEase.Contracts.Stores;

/// <summary>A user who runs a store, as <c>/stores/{storeId}/members</c> returns it.</summary>
public sealed record StoreMemberResponse(Guid StoreId, Guid UserId, string FirstName, string LastName, string Email);
