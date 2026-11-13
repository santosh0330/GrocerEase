using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>Lists a customer's own orders, newest first. The customer is the caller: there is no way to ask for someone else's.</summary>
public sealed class ListMyOrdersQueryHandler(IOrderRepository orderRepository)
    : ICommandHandler<ListMyOrdersQuery, Result<PagedResult<OrderResult>>>
{
    public async Task<Result<PagedResult<OrderResult>>> Handle(ListMyOrdersQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var page = await orderRepository.ListForCustomerAsync(command.CustomerId, command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(order => order.ToResult()));
    }
}
