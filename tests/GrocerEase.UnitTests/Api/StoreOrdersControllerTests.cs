using System.Reflection;
using System.Security.Claims;
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

public class StoreOrdersControllerTests
{
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid CallerId = Guid.CreateVersion7();

    private sealed class ListSpy(Result<PagedResult<OrderResult>> result) : ICommandHandler<ListStoreOrdersQuery, Result<PagedResult<OrderResult>>>
    {
        public ListStoreOrdersQuery? Received { get; private set; }

        public Task<Result<PagedResult<OrderResult>>> Handle(ListStoreOrdersQuery command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static StoreOrdersController ControllerWith(ListSpy list, string? sub = null)
    {
        var controller = new StoreOrdersController(list)
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(sub is null ? [] : [new Claim("sub", sub)], authenticationType: "Bearer"));
        return controller;
    }

    private static readonly PagedResult<OrderResult> Empty = new([], 1, 20, 0);

    [Fact]
    public async Task List_PutsTheStoreTheCallerTheStatusAndThePagingInTheQuery_AndReturnsThePageMapped()
    {
        var placedAt = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
        var order = new OrderResult(Guid.CreateVersion7(), StoreId, "Pending", "1 High Street", placedAt, 5m, [new OrderLineResult(Guid.CreateVersion7(), "Milk", 2.5m, 2, 5m)]);
        var list = new ListSpy(Result.Success(new PagedResult<OrderResult>([order], 2, 5, 11)));

        var result = await ControllerWith(list, CallerId.ToString()).List(StoreId, CancellationToken.None, "Pending", 2, 5);

        Assert.Equal(new ListStoreOrdersQuery(StoreId, CallerId, "Pending", 2, 5), list.Received);
        var body = Assert.IsType<PagedResponse<OrderResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((order.Id, "Pending", 5m), (body.Items[0].Id, body.Items[0].Status, body.Items[0].Total));
        Assert.Equal((2, 5, 11), (body.Page, body.PageSize, body.TotalCount));
    }

    [Fact]
    public async Task List_WithNoStatus_AsksForEveryStatus()
    {
        var list = new ListSpy(Result.Success(Empty));

        await ControllerWith(list, CallerId.ToString()).List(StoreId, CancellationToken.None);

        Assert.Equal(new ListStoreOrdersQuery(StoreId, CallerId, null, 1, Paging.DefaultPageSize), list.Received);
    }

    [Fact]
    public async Task List_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var list = new ListSpy(Result.Success(Empty));

        var result = await ControllerWith(list).List(StoreId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(list.Received);
    }

    [Fact]
    public async Task List_AStoreThatIsNotTheirs_Is404()
    {
        var list = new ListSpy(Result.Failure<PagedResult<OrderResult>>(GrocerEase.Application.Stores.Common.StoreErrors.NotFound));

        var result = await ControllerWith(list, CallerId.ToString()).List(StoreId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
    }

    [Fact]
    public void EachRoute_NeedsThePermissionToManageOrders_NotARoleName()
    {
        var authorize = typeof(StoreOrdersController).GetMethod(nameof(StoreOrdersController.List))!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.OrdersManage, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }
}
