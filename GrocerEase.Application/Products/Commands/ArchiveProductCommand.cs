namespace GrocerEase.Application.Products.Commands;

/// <summary>Intent to take a product off sale ("delete" it). <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.</summary>
public sealed record ArchiveProductCommand(Guid StoreId, Guid ProductId, Guid ActorId);
