namespace GrocerEase.Application.Stores.Commands;

/// <summary>Intent to create a store. Application's own type: it must not reference Contracts.</summary>
public sealed record CreateStoreCommand(string Name, string Address);
