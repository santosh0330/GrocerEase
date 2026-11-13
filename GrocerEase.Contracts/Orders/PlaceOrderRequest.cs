namespace GrocerEase.Contracts.Orders;

/// <summary>Body of <c>POST /stores/{storeId}/orders</c>. A product may appear once: ask for a bigger quantity instead of a second line.</summary>
public sealed record PlaceOrderRequest(string DeliveryAddress, IReadOnlyList<OrderLineRequest> Lines);
