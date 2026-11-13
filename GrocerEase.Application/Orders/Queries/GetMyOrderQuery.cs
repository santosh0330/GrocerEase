namespace GrocerEase.Application.Orders.Queries;

/// <summary>Intent to read one of the signed-in customer's own orders.</summary>
public sealed record GetMyOrderQuery(Guid CustomerId, Guid OrderId);
