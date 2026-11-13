using System.Reflection;
using GrocerEase.Api.Controllers;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class AdminOrdersControllerTests
{
    private sealed class ListSpy(Result<PagedResult<OrderResult>> result) : ICommandHandler<ListAllOrdersQuery, Result<PagedResult<OrderResult>>>
    {
        public ListAllOrdersQuery? Received { get; private set; }

        public Task<Result<PagedResult<OrderResult>>> Handle(ListAllOrdersQuery command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static AdminOrdersController ControllerWith(ListSpy list) =>
        new(list)
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task List_PutsTheStoreTheStatusAndThePagingInTheQuery_AndReturnsThePageMapped()
    {
        var storeId = Guid.CreateVersion7();
        var order = new OrderResult(
            Guid.CreateVersion7(), storeId, "Pending", "1 High Street", new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero), 5m, [new OrderLineResult(Guid.CreateVersion7(), "Milk", 2.5m, 2, 5m)]);
        var list = new ListSpy(Result.Success(new PagedResult<OrderResult>([order], 2, 5, 11)));

        var result = await ControllerWith(list).List(CancellationToken.None, storeId, "Pending", 2, 5);

        Assert.Equal(new ListAllOrdersQuery(storeId, "Pending", 2, 5), list.Received);
        var body = Assert.IsType<PagedResponse<OrderResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((order.Id, storeId, "Pending", 5m), (body.Items[0].Id, body.Items[0].StoreId, body.Items[0].Status, body.Items[0].Total));
        Assert.Equal((2, 5, 11), (body.Page, body.PageSize, body.TotalCount));
    }

    [Fact]
    public async Task List_WithNoFilters_AsksForEveryStoreAndEveryStatus()
    {
        var list = new ListSpy(Result.Success(new PagedResult<OrderResult>([], 1, Paging.DefaultPageSize, 0)));

        await ControllerWith(list).List(CancellationToken.None);

        Assert.Equal(new ListAllOrdersQuery(null, null, 1, Paging.DefaultPageSize), list.Received);
    }

    [Fact]
    public async Task List_WhenTheHandlerFails_ReturnsAProblemForThatError()
    {
        var list = new ListSpy(Result.Failure<PagedResult<OrderResult>>(new ResultError("Order.Broken", "x", ErrorType.Conflict)));

        var result = await ControllerWith(list).List(CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
    }

    [Fact]
    public void TheRoute_NeedsThePermissionToReadEveryOrder_NotARoleName_AndNotThePermissionToManageOne()
    {
        var authorize = typeof(AdminOrdersController).GetMethod(nameof(AdminOrdersController.List))!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.OrdersReadAll, authorize?.Policy);
        Assert.NotEqual(Permissions.OrdersManage, authorize?.Policy, StringComparer.Ordinal);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void TheController_HasOnlyReadRoutes()
    {
        var routes = typeof(AdminOrdersController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SingleOrDefault())
            .Where(attribute => attribute is not null).SelectMany(attribute => attribute!.HttpMethods).ToList();

        Assert.Equal(["GET"], routes);
    }
}
