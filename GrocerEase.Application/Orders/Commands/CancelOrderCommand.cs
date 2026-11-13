namespace GrocerEase.Application.Orders.Commands;

/// <summary>Intent to cancel an order of a store that has not shipped. <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.</summary>
public sealed record CancelOrderCommand(Guid StoreId, Guid OrderId, Guid ActorId);
