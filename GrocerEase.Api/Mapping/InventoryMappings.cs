using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class InventoryMappings
{
    public static AdjustStockCommand ToCommand(this AdjustStockRequest request, Guid storeId, Guid productId, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AdjustStockCommand(storeId, productId, actorId, request.Delta);
    }

    public static SetLowStockThresholdCommand ToCommand(this SetLowStockThresholdRequest request, Guid storeId, Guid productId, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SetLowStockThresholdCommand(storeId, productId, actorId, request.Threshold);
    }

    public static InventoryEntryResponse ToResponse(this InventoryEntryResult entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new InventoryEntryResponse(entry.ProductId, entry.ProductName, entry.QuantityOnHand, entry.LowStockThreshold, entry.IsLow);
    }

    public static PagedResponse<InventoryEntryResponse> ToResponse(this PagedResult<InventoryEntryResult> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PagedResponse<InventoryEntryResponse>(
            [.. page.Items.Select(entry => entry.ToResponse())], page.Page, page.PageSize, page.TotalCount);
    }

    public static StockResponse ToResponse(this StockResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new StockResponse(result.ProductId, result.QuantityOnHand);
    }
}
