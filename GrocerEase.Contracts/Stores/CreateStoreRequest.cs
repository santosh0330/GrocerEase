namespace GrocerEase.Contracts.Stores;

/// <summary>Body of <c>POST /stores</c>.</summary>
public sealed record CreateStoreRequest(string Name, string Address);
