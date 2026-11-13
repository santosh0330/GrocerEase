namespace GrocerEase.Application.Orders.Commands;

/// <summary>
/// Intent to order products from one store. <paramref name="CustomerId"/> is the signed-in user who asks: it comes from their token, never
/// from the body, so nobody can place an order in someone else's name. Application's own type: it must not reference Contracts.
/// </summary>
public sealed record PlaceOrderCommand(Guid StoreId, Guid CustomerId, string DeliveryAddress, IReadOnlyList<OrderLineInput> Lines);
