namespace GrocerEase.Application.Products.Commands;

/// <summary>
/// Intent to add a product to a store. <paramref name="ActorId"/> is the signed-in user who asks: the use case checks that they run
/// the store. Application's own type: it must not reference Contracts.
/// </summary>
public sealed record CreateProductCommand(Guid StoreId, Guid ActorId, string Name, string Description, decimal Price);
