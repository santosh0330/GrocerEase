namespace GrocerEase.Application.Products.Commands;

/// <summary>Intent to change a product's name, description and price. <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.</summary>
public sealed record UpdateProductCommand(Guid StoreId, Guid ProductId, Guid ActorId, string Name, string Description, decimal Price);
