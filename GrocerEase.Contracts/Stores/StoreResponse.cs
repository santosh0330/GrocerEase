namespace GrocerEase.Contracts.Stores;

/// <summary>One store, as <c>/stores</c> returns it.</summary>
public sealed record StoreResponse(Guid Id, string Name, string Address);
