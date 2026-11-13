namespace GrocerEase.Application.Products.Commands;

/// <summary>
/// Intent to change a product's name, description and price. <paramref name="ActorId"/> is the signed-in user: the use case checks that they
/// run the store. <paramref name="Version"/> is the version of the product the person read: the change is made on top of that one, or
/// refused if the product has moved on.
/// </summary>
public sealed record UpdateProductCommand(Guid StoreId, Guid ProductId, Guid ActorId, string Name, string Description, decimal Price, int Version);
