namespace GrocerEase.Domain.Entities;

/// <summary>
/// Why a quantity changed. Stored as its name, so a row reads in SQL. The first is written once, by the migration that made the ledger;
/// the next two are what a store user says when they change stock by hand; the last two are what an order does.
/// </summary>
public enum StockMovementReason
{
    /// <summary>The stock a product already had when the ledger began, so that the ledger adds up from day one. No one did it.</summary>
    OpeningBalance,

    /// <summary>Units arrived: a delivery. Always an addition.</summary>
    Received,

    /// <summary>The count was wrong, or something was lost or broken. Either direction.</summary>
    Correction,

    /// <summary>A customer's order took the stock. Always a removal, and always for an order.</summary>
    OrderPlaced,

    /// <summary>A cancelled order gave the stock back. Always an addition, and always for an order.</summary>
    OrderCancelled,
}
