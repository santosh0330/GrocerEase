using System.Reflection;
using System.Security.Claims;
using GrocerEase.Api.Controllers;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
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

    private sealed class MoveSpy(Result<OrderResult> result) : ICommandHandler<VerifyOrderCommand, Result<OrderResult>>
    {
        public VerifyOrderCommand? Received { get; private set; }

        public Task<Result<OrderResult>> Handle(VerifyOrderCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly Guid OrderId = Guid.CreateVersion7();

    private static OrderResult AnOrder(string status = "Verified") =>
        new(OrderId, StoreId, status, "1 High Street", new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero), 5m, [new OrderLineResult(Guid.CreateVersion7(), "Milk", 2.5m, 2, 5m)]);

    private static StoreOrdersController ControllerWith(ListSpy list, string? sub = null, MoveSpy? verify = null)
    {
        var controller = new StoreOrdersController(list, verify ?? new MoveSpy(Result.Success(AnOrder())))
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
    public async Task Verify_PutsTheRouteAndTheCallerInTheCommand_AndReturnsTheOrder()
    {
        var verify = new MoveSpy(Result.Success(AnOrder()));

        var result = await ControllerWith(new ListSpy(Result.Success(Empty)), CallerId.ToString(), verify).Verify(StoreId, OrderId, CancellationToken.None);

        Assert.Equal(new VerifyOrderCommand(StoreId, OrderId, CallerId), verify.Received);
        var body = Assert.IsType<OrderResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((OrderId, "Verified"), (body.Id, body.Status));
    }

    [Fact]
    public async Task Verify_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var verify = new MoveSpy(Result.Success(AnOrder()));

        var result = await ControllerWith(new ListSpy(Result.Success(Empty)), verify: verify).Verify(StoreId, OrderId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(verify.Received);
    }

    [Theory]
    [InlineData("Store.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Order.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Order.InvalidTransition", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData("Order.ChangedMeanwhile", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Verify_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var verify = new MoveSpy(Result.Failure<OrderResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(new ListSpy(Result.Success(Empty)), CallerId.ToString(), verify).Verify(StoreId, OrderId, CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Theory]
    [InlineData(nameof(StoreOrdersController.List))]
    [InlineData(nameof(StoreOrdersController.Verify))]
    public void EachRoute_NeedsThePermissionToManageOrders_NotARoleName(string action)
    {
        var authorize = typeof(StoreOrdersController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.OrdersManage, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }
}
