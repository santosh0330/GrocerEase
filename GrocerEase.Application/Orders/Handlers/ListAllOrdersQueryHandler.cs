using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Handlers;

/// <summary>
/// Lists the orders of every store for the admin, newest first, optionally of one store and in one status. It reads only: the admin does not
/// verify, ship or cancel. There is no membership check, because the admin runs no store; the permission on the route is the check.
/// </summary>
public sealed class ListAllOrdersQueryHandler(IAdminOrderReader reader)
    : ICommandHandler<ListAllOrdersQuery, Result<PagedResult<OrderResult>>>
{
    public async Task<Result<PagedResult<OrderResult>>> Handle(ListAllOrdersQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The validator has checked the name, so the parse cannot fail.
        OrderStatus? status = command.Status is null ? null : Enum.Parse<OrderStatus>(command.Status, ignoreCase: true);

        var page = await reader.ListAsync(command.StoreId, status, command.Page, command.PageSize, cancellationToken);
        return Result.Success(page.Map(order => order.ToResult()));
    }
}
