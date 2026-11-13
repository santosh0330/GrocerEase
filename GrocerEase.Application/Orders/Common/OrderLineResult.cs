namespace GrocerEase.Application.Orders.Common;

/// <summary>One line of an order: a product as it was when it was bought.</summary>
public sealed record OrderLineResult(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
