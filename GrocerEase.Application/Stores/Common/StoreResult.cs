namespace GrocerEase.Application.Stores.Common;

/// <summary>A store as the use cases hand it back. Application's own type: it must not reference Contracts.</summary>
public sealed record StoreResult(Guid Id, string Name, string Address);
