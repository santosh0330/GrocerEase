namespace GrocerEase.Contracts.Inventory;

/// <summary>Body of <c>PUT /stores/{storeId}/products/{productId}/low-stock-threshold</c>: the level at or below which the product counts as low, or <c>null</c> to clear it.</summary>
public sealed record SetLowStockThresholdRequest(int? Threshold);
