using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>Reads one of a customer's own orders. Someone else's order is not found, exactly as an order that does not exist.</summary>
public sealed class GetMyOrderQueryHandler(IOrderRepository orderRepository) : ICommandHandler<GetMyOrderQuery, Result<OrderResult>>
{
    public async Task<Result<OrderResult>> Handle(GetMyOrderQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orderRepository.GetForCustomerAsync(command.CustomerId, command.OrderId, cancellationToken);
        return order is null ? Result.Failure<OrderResult>(OrderErrors.NotFound) : Result.Success(order.ToResult());
    }
}
