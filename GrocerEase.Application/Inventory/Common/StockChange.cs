using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Common;

/// <summary>
/// A change to the quantity of a product and everything the ledger needs to explain it: by how much (<paramref name="Delta"/>, negative to
/// remove), why, who did it, and which order it was for. The repository writes the quantity and the movement in one statement.
/// </summary>
/// <param name="Delta">By how much; never zero.</param>
/// <param name="Reason">Why. Not the opening balance: only the migration writes that.</param>
/// <param name="ActorId">The signed-in user behind the change: the store user adjusting by hand, or the customer or store user behind an order.</param>
/// <param name="OrderId">The order that took or returned the stock; <c>null</c> for a change by hand.</param>
public sealed record StockChange(int Delta, StockMovementReason Reason, Guid ActorId, Guid? OrderId = null);
