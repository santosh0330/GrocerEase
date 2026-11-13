namespace GrocerEase.Application.Orders.Commands;

/// <summary>One line of an order being placed: which product and how many.</summary>
public sealed record OrderLineInput(Guid ProductId, int Quantity);
