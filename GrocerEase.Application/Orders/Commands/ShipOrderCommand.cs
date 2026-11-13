namespace GrocerEase.Application.Orders.Commands;

/// <summary>Intent to send a verified order of a store. <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.</summary>
public sealed record ShipOrderCommand(Guid StoreId, Guid OrderId, Guid ActorId);
