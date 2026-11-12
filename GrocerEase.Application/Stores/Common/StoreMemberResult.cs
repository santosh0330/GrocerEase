namespace GrocerEase.Application.Stores.Common;

/// <summary>A user who runs a store, as the use cases hand it back. Never carries the password hash.</summary>
public sealed record StoreMemberResult(Guid StoreId, Guid UserId, string FirstName, string LastName, string Email);
