using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class OrderMappings
{
    public static PlaceOrderCommand ToCommand(this PlaceOrderRequest request, Guid storeId, Guid customerId)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A body can be missing its lines, or hold a null where a line should be. Neither may become a 500: the validator says what is wrong.
        IReadOnlyList<OrderLineInput> lines =
        [
            .. (request.Lines ?? []).Select(line => line is null ? new OrderLineInput(Guid.Empty, 0) : new OrderLineInput(line.ProductId, line.Quantity)),
        ];
        return new PlaceOrderCommand(storeId, customerId, request.DeliveryAddress, lines);
    }

    public static PagedResponse<OrderResponse> ToResponse(this PagedResult<OrderResult> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PagedResponse<OrderResponse>([.. page.Items.Select(order => order.ToResponse())], page.Page, page.PageSize, page.TotalCount);
    }

    public static OrderResponse ToResponse(this OrderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new OrderResponse(
            result.Id,
            result.StoreId,
            result.Status,
            result.DeliveryAddress,
            result.PlacedAt,
            result.Total,
            [.. result.Lines.Select(line => new OrderLineResponse(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity, line.LineTotal))]);
    }
}
