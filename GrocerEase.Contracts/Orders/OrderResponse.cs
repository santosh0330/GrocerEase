namespace GrocerEase.Contracts.Orders;

/// <summary>An order, as <c>/orders</c> returns it. <c>status</c> is <c>Pending</c>, <c>Verified</c>, <c>Shipped</c> or <c>Cancelled</c>.</summary>
public sealed record OrderResponse(
    Guid Id, Guid StoreId, string Status, string DeliveryAddress, DateTimeOffset PlacedAt, decimal Total, IReadOnlyList<OrderLineResponse> Lines);
