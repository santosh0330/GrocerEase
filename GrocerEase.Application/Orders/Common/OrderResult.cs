namespace GrocerEase.Application.Orders.Common;

/// <summary>An order as the use cases hand it back. Application's own type: it must not reference Contracts.</summary>
public sealed record OrderResult(
    Guid Id,
    Guid StoreId,
    string Status,
    string DeliveryAddress,
    DateTimeOffset PlacedAt,
    decimal Total,
    IReadOnlyList<OrderLineResult> Lines);
