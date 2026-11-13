namespace GrocerEase.Domain.Entities;

/// <summary>
/// Where an order is in its life. A new order is <see cref="Pending"/>; the store user moves it to <see cref="Verified"/>, then
/// <see cref="Shipped"/>, or <see cref="Cancelled"/> while it is not shipped. Stored as its name, so a row reads in SQL.
/// </summary>
public enum OrderStatus
{
    /// <summary>Placed by the customer, not yet looked at by the store. Its stock is already reserved.</summary>
    Pending,

    /// <summary>The store accepted it.</summary>
    Verified,

    /// <summary>On its way: final.</summary>
    Shipped,

    /// <summary>Refused or withdrawn before it shipped: final, and its stock is back.</summary>
    Cancelled,
}
