using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// Lists the orders of a store for the people who run it, oldest first, optionally in one status. A store user sees the orders of the
/// stores they run and no others: a store they do not run is the same <c>Store.NotFound</c> as one that does not exist.
/// </summary>
public sealed class ListStoreOrdersQueryHandler(StoreAccess storeAccess, IOrderRepository orderRepository)
    : ICommandHandler<ListStoreOrdersQuery, Result<PagedResult<OrderResult>>>
{
    public async Task<Result<PagedResult<OrderResult>>> Handle(ListStoreOrdersQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeAccess.RequireMemberAsync(command.StoreId, command.ActorId, cancellationToken) is { } notAMember)
        {
            return Result.Failure<PagedResult<OrderResult>>(notAMember);
        }

        // The validator has checked the name, so the parse cannot fail.
        OrderStatus? status = command.Status is null ? null : Enum.Parse<OrderStatus>(command.Status, ignoreCase: true);

        var page = await orderRepository.ListForStoreAsync(command.StoreId, status, command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(order => order.ToResult()));
    }
}
