namespace GrocerEase.Contracts.Orders;

/// <summary>One line of an order: a product as it was when it was bought.</summary>
public sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
