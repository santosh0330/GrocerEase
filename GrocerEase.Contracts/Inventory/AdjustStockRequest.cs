namespace GrocerEase.Contracts.Inventory;

/// <summary>Body of <c>POST /stores/{storeId}/products/{productId}/stock-adjustments</c>: how many to add (positive) or remove (negative).</summary>
public sealed record AdjustStockRequest(int Delta);
