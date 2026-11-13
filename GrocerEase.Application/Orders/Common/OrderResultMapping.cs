using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Common;

internal static class OrderResultMapping
{
    public static OrderResult ToResult(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Lines in a stable order (by name, then id), whichever order the database returned them in.
        IReadOnlyList<OrderLineResult> lines =
        [
            .. order.Lines.OrderBy(line => line.ProductName, StringComparer.Ordinal).ThenBy(line => line.ProductId)
                .Select(line => new OrderLineResult(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity, line.LineTotal)),
        ];
        return new OrderResult(order.Id, order.StoreId, order.Status.ToString(), order.DeliveryAddress, order.PlacedAt, order.Total, lines);
    }
}
