namespace GrocerEase.Application.Orders.Commands;

/// <summary>Intent of a customer to cancel their own order while it is pending. <paramref name="CustomerId"/> is the signed-in user, never read from the body.</summary>
public sealed record CancelMyOrderCommand(Guid CustomerId, Guid OrderId);
