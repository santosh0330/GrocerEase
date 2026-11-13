using System.Reflection;
using System.Security.Claims;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
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

public class OrdersControllerTests
{
    private sealed class Spy<TCommand>(Result<OrderResult> result) : ICommandHandler<TCommand, Result<OrderResult>>
    {
        public TCommand? Received { get; private set; }

        public Task<Result<OrderResult>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly OrderResult Placed = new(
        Guid.CreateVersion7(), StoreId, "Pending", "1 High Street", Now, 5.00m, [new OrderLineResult(ProductId, "Milk", 2.50m, 2, 5.00m)]);

    private sealed class ListSpy(Result<PagedResult<OrderResult>> result) : ICommandHandler<ListMyOrdersQuery, Result<PagedResult<OrderResult>>>
    {
        public ListMyOrdersQuery? Received { get; private set; }

        public Task<Result<PagedResult<OrderResult>>> Handle(ListMyOrdersQuery command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static OrdersController ControllerWith(
        Spy<PlaceOrderCommand> place, Spy<GetMyOrderQuery>? get = null, string? sub = null, ListSpy? list = null, Spy<CancelMyOrderCommand>? cancel = null)
    {
        var controller = new OrdersController(
            place,
            get ?? new Spy<GetMyOrderQuery>(Result.Success(Placed)),
            list ?? new ListSpy(Result.Success(new PagedResult<OrderResult>([], 1, 20, 0))),
            cancel ?? new Spy<CancelMyOrderCommand>(Result.Success(Placed with { Status = "Cancelled" })))
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(sub is null ? [] : [new Claim("sub", sub)], authenticationType: "Bearer"));
        return controller;
    }

    private static readonly PlaceOrderRequest Request = new("1 High Street", [new OrderLineRequest(ProductId, 2)]);

    [Fact]
    public async Task Place_PutsTheStoreFromTheRoute_TheCustomerFromTheToken_AndTheBodyInTheCommand()
    {
        var spy = new Spy<PlaceOrderCommand>(Result.Success(Placed));

        var result = await ControllerWith(spy, sub: CustomerId.ToString()).Place(StoreId, Request, CancellationToken.None);

        Assert.Equal((StoreId, CustomerId, "1 High Street"), (spy.Received?.StoreId, spy.Received?.CustomerId, spy.Received?.DeliveryAddress));
        Assert.Equal([new OrderLineInput(ProductId, 2)], spy.Received?.Lines);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(OrdersController.Get), created.ActionName);
        Assert.Equal(Placed.Id, created.RouteValues?["orderId"]);
        var body = Assert.IsType<OrderResponse>(created.Value);
        Assert.Equal(("Pending", 5.00m), (body.Status, body.Total));
        Assert.Equal([new OrderLineResponse(ProductId, "Milk", 2.50m, 2, 5.00m)], body.Lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-id")]
    public async Task Place_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing(string? sub)
    {
        var spy = new Spy<PlaceOrderCommand>(Result.Success(Placed));

        var result = await ControllerWith(spy, sub: sub).Place(StoreId, Request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(spy.Received);
    }

    [Theory]
    [InlineData("Store.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Product.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Order.InsufficientStock", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Place_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var spy = new Spy<PlaceOrderCommand>(Result.Failure<OrderResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(spy, sub: CustomerId.ToString()).Place(StoreId, Request, CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task Get_PutsTheCustomerFromTheTokenAndTheOrderFromTheRouteInTheQuery()
    {
        var get = new Spy<GetMyOrderQuery>(Result.Success(Placed));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), get, CustomerId.ToString()).Get(Placed.Id, CancellationToken.None);

        Assert.Equal(new GetMyOrderQuery(CustomerId, Placed.Id), get.Received);
        Assert.Equal(Placed.Id, Assert.IsType<OrderResponse>(Assert.IsType<OkObjectResult>(result).Value).Id);
    }

    [Fact]
    public async Task Get_WithATokenThatDoesNotNameAUser_Is401_AndSomeoneElsesOrderIs404()
    {
        var noUser = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed))).Get(Placed.Id, CancellationToken.None);
        var get = new Spy<GetMyOrderQuery>(Result.Failure<OrderResult>(OrderErrors.NotFound));
        var notMine = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), get, CustomerId.ToString()).Get(Placed.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(noUser).Value).Status);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(notMine).Value).Status);
    }

    [Fact]
    public async Task List_PutsTheCustomerFromTheTokenAndThePagingInTheQuery_AndReturnsThePageMapped()
    {
        var list = new ListSpy(Result.Success(new PagedResult<OrderResult>([Placed], 2, 5, 11)));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), sub: CustomerId.ToString(), list: list).List(CancellationToken.None, 2, 5);

        Assert.Equal(new ListMyOrdersQuery(CustomerId, 2, 5), list.Received); // the customer is the token's: there is nothing in the request to ask for someone else's
        var body = Assert.IsType<PagedResponse<OrderResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([Placed.Id], body.Items.Select(order => order.Id));
        Assert.Equal((2, 5, 11), (body.Page, body.PageSize, body.TotalCount));
    }

    [Fact]
    public async Task List_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var list = new ListSpy(Result.Success(new PagedResult<OrderResult>([], 1, 20, 0)));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), list: list).List(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(list.Received);
    }

    [Fact]
    public async Task Cancel_PutsTheOrderAndTheCallerFromTheTokenInTheCommand_AndReturnsTheOrder()
    {
        var cancel = new Spy<CancelMyOrderCommand>(Result.Success(Placed with { Status = "Cancelled" }));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), sub: CustomerId.ToString(), cancel: cancel).Cancel(Placed.Id, CancellationToken.None);

        Assert.Equal(new CancelMyOrderCommand(CustomerId, Placed.Id), cancel.Received);
        Assert.Equal("Cancelled", Assert.IsType<OrderResponse>(Assert.IsType<OkObjectResult>(result).Value).Status);
    }

    [Fact]
    public async Task Cancel_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var cancel = new Spy<CancelMyOrderCommand>(Result.Success(Placed));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), cancel: cancel).Cancel(Placed.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(cancel.Received);
    }

    [Theory]
    [InlineData("Order.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Order.InvalidTransition", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData("Order.ChangedMeanwhile", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData("Order.CannotRestoreStock", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Cancel_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var cancel = new Spy<CancelMyOrderCommand>(Result.Failure<OrderResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(new Spy<PlaceOrderCommand>(Result.Success(Placed)), sub: CustomerId.ToString(), cancel: cancel).Cancel(Placed.Id, CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Theory]
    [InlineData(nameof(OrdersController.Place), Permissions.OrdersPlace)]
    [InlineData(nameof(OrdersController.Cancel), Permissions.OrdersCancel)]
    [InlineData(nameof(OrdersController.List), Permissions.OrdersRead)]
    [InlineData(nameof(OrdersController.Get), Permissions.OrdersRead)]
    public void EachRoute_NeedsItsPermission_NotARoleName(string action, string permission)
    {
        var authorize = typeof(OrdersController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(permission, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void TheMapping_SurvivesABodyWithNoLinesOrANullLine_SoTheValidatorCanSayWhatIsWrong()
    {
        var noLines = new PlaceOrderRequest("x", null!).ToCommand(StoreId, CustomerId);
        var nullLine = new PlaceOrderRequest("x", [null!]).ToCommand(StoreId, CustomerId);

        Assert.Empty(noLines.Lines);
        Assert.Equal([new OrderLineInput(Guid.Empty, 0)], nullLine.Lines); // invalid on purpose: it becomes a 400, not a NullReferenceException
    }
}
