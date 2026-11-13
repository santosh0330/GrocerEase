namespace GrocerEase.Contracts.Orders;

/// <summary>One line of an order being placed: which product and how many.</summary>
public sealed record OrderLineRequest(Guid ProductId, int Quantity);
