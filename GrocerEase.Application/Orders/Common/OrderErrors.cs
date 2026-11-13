using GrocerEase.Application.Common;

namespace GrocerEase.Application.Orders.Common;

/// <summary>The expected failures of the order use cases. Codes are stable: clients may switch on them.</summary>
public static class OrderErrors
{
    /// <summary>
    /// A line asked for more than the store has. It names the product (one the customer asked for, and can see in the catalogue), so
    /// they know which line to change; the code is the same whichever product it is.
    /// </summary>
    public static ResultError InsufficientStock(string productName) => new(
        "Order.InsufficientStock", $"There is not enough stock of '{productName}'.", ErrorType.Conflict);

    // One error for an order that never existed and one that belongs to someone else, so nobody can probe for other customers' orders.
    public static readonly ResultError NotFound = new(
        "Order.NotFound", "There is no such order.", ErrorType.NotFound);
}
