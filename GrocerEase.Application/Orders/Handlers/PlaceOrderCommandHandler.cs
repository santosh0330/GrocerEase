using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// Places an order: it reserves the stock of every line and records the order, <b>all or nothing</b>. It is registered as a transactional
/// handler, so if the third line cannot be filled, the stock already taken for the first two goes back and no order exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the reservation is a statement and not a check.</b> Two customers can both see "1 left". Each line is reserved with
/// <see cref="IInventoryItemRepository.TryAdjustAsync"/>, one SQL statement with <c>quantity - n &gt;= 0</c> inside it, so only one of
/// them gets the last item. Taking the stock when the order is <i>placed</i> (not when it is verified) is what makes an order a
/// promise the store can keep.
/// </para>
/// <para>
/// <b>Why the lines are reserved in a fixed order.</b> A row is locked until the unit ends. Two orders that share two products and ask
/// for them in opposite orders would each hold one row and wait for the other: a deadlock. Both take the product with the lower id
/// first, so one simply waits for the other.
/// </para>
/// </remarks>
public sealed class PlaceOrderCommandHandler(
    IStoreRepository storeRepository,
    IProductRepository productRepository,
    IInventoryItemRepository inventoryRepository,
    IOrderRepository orderRepository,
    TimeProvider timeProvider) : ICommandHandler<PlaceOrderCommand, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Anyone may order from any store that exists: there is no ownership rule for a customer ordering, only a store to order from.
        if (await storeRepository.GetByIdAsync(command.StoreId, cancellationToken) is null)
        {
            return Result.Failure<OrderResult>(StoreErrors.NotFound);
        }

        // Only products on sale in THIS store: an archived one, an unknown one and another store's are all "not available".
        var wanted = command.Lines.Select(line => line.ProductId).ToList();
        var products = (await productRepository.GetLiveByIdsAsync(command.StoreId, wanted, cancellationToken)).ToDictionary(product => product.Id);
        if (products.Count != wanted.Count)
        {
            return Result.Failure<OrderResult>(ProductErrors.NotFound);
        }

        // The name and the price are copied as they are now, so a later change does not rewrite what was ordered.
        var orderId = Guid.CreateVersion7();
        var lines = command.Lines.OrderBy(line => line.ProductId)
            .Select(line => new OrderLine(Guid.CreateVersion7(), orderId, line.ProductId, products[line.ProductId].Name, products[line.ProductId].Price, line.Quantity))
            .ToList();

        foreach (var line in lines)
        {
            if (await inventoryRepository.TryAdjustAsync(command.StoreId, line.ProductId, -line.Quantity, cancellationToken) is null)
            {
                // The decorator rolls back everything reserved before this line.
                return Result.Failure<OrderResult>(OrderErrors.InsufficientStock(line.ProductName));
            }
        }

        var order = Order.Place(orderId, command.StoreId, command.CustomerId, command.DeliveryAddress, timeProvider.GetUtcNow(), lines);
        await orderRepository.AddAsync(order, cancellationToken);
        return Result.Success(order.ToResult());
    }
}
