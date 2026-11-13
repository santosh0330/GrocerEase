using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

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

    /// <summary>The order is in a status the move cannot start from: a shipped order cannot be cancelled, a pending one cannot be shipped.</summary>
    public static ResultError InvalidTransition(string pastTense, OrderStatus current) => new(
        "Order.InvalidTransition", $"An order that is {current} cannot be {pastTense}.", ErrorType.Conflict);

    /// <summary>Someone else moved the order between this request reading it and changing it. Nothing was changed.</summary>
    public static readonly ResultError ChangedMeanwhile = new(
        "Order.ChangedMeanwhile", "The order was changed by someone else just now. Look at it again.", ErrorType.Conflict);

    /// <summary>
    /// Putting the stock of a cancelled line back would take it past the most a store can hold, because the store has added stock since. The
    /// order is not cancelled: nothing is lost, and the store can make room and try again.
    /// </summary>
    public static ResultError CannotRestoreStock(string productName) => new(
        "Order.CannotRestoreStock", $"The stock of '{productName}' is too high to put the cancelled quantity back. Remove some stock and cancel again.", ErrorType.Conflict);

    // One error for an order that never existed and one that belongs to someone else, so nobody can probe for other customers' orders.
    public static readonly ResultError NotFound = new(
        "Order.NotFound", "There is no such order.", ErrorType.NotFound);
}
