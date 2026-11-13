namespace GrocerEase.Contracts.Inventory;

/// <summary>
/// Body of <c>POST /stores/{storeId}/products/{productId}/stock-adjustments</c>: how many to add (positive) or remove (negative), and, if
/// the person wants to say why, <c>Received</c> (a delivery: always an addition) or <c>Correction</c> (the count was wrong, or something was
/// lost: either way). Left out, a change that adds is recorded as a delivery and one that removes as a correction.
/// </summary>
public sealed record AdjustStockRequest(int Delta, string? Reason = null);
