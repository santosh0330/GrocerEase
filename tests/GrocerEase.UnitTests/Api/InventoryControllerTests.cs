using System.Reflection;
using System.Security.Claims;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class InventoryControllerTests
{
    private sealed class Spy(Result<StockResult> result) : ICommandHandler<AdjustStockCommand, Result<StockResult>>
    {
        public AdjustStockCommand? Received { get; private set; }

        public Task<Result<StockResult>> Handle(AdjustStockCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid CallerId = Guid.CreateVersion7();

    private sealed class ListSpy(Result<PagedResult<InventoryEntryResult>> result) : ICommandHandler<ListInventoryQuery, Result<PagedResult<InventoryEntryResult>>>
    {
        public ListInventoryQuery? Received { get; private set; }

        public Task<Result<PagedResult<InventoryEntryResult>>> Handle(ListInventoryQuery command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private sealed class ThresholdSpy(Result<InventoryEntryResult> result) : ICommandHandler<SetLowStockThresholdCommand, Result<InventoryEntryResult>>
    {
        public SetLowStockThresholdCommand? Received { get; private set; }

        public Task<Result<InventoryEntryResult>> Handle(SetLowStockThresholdCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static InventoryController ControllerWith(Spy adjust, string? sub = null, ListSpy? list = null, ThresholdSpy? threshold = null)
    {
        var controller = new InventoryController(
            adjust,
            list ?? new ListSpy(Result.Success(new PagedResult<InventoryEntryResult>([], 1, 20, 0))),
            threshold ?? new ThresholdSpy(Result.Success(new InventoryEntryResult(ProductId, "Milk", 7, 5))))
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(sub is null ? [] : [new Claim("sub", sub)], authenticationType: "Bearer"));
        return controller;
    }

    [Fact]
    public async Task Adjust_PutsTheRouteTheCallerAndTheAmountInTheCommand_AndReturnsTheQuantity()
    {
        var spy = new Spy(Result.Success(new StockResult(ProductId, 7)));

        var result = await ControllerWith(spy, CallerId.ToString()).Adjust(StoreId, ProductId, new AdjustStockRequest(-3), CancellationToken.None);

        Assert.Equal(new AdjustStockCommand(StoreId, ProductId, CallerId, -3), spy.Received);
        Assert.Equal(new StockResponse(ProductId, 7), Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Adjust_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var spy = new Spy(Result.Success(new StockResult(ProductId, 7)));

        var result = await ControllerWith(spy).Adjust(StoreId, ProductId, new AdjustStockRequest(1), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(spy.Received);
    }

    [Theory]
    [InlineData("Store.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Product.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Inventory.NotEnoughStock", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData("Inventory.TooMuchStock", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Adjust_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var spy = new Spy(Result.Failure<StockResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(spy, CallerId.ToString()).Adjust(StoreId, ProductId, new AdjustStockRequest(1), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task List_PutsTheStoreTheCallerAndThePagingInTheQuery_AndReturnsThePageMapped()
    {
        var list = new ListSpy(Result.Success(new PagedResult<InventoryEntryResult>([new InventoryEntryResult(ProductId, "Milk", 7)], 2, 5, 11)));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), CallerId.ToString(), list).List(StoreId, CancellationToken.None, 2, 5);

        Assert.Equal(new ListInventoryQuery(StoreId, CallerId, 2, 5), list.Received);
        var body = Assert.IsType<PagedResponse<InventoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([new InventoryEntryResponse(ProductId, "Milk", 7, null, false)], body.Items);
        Assert.Equal((2, 5, 11), (body.Page, body.PageSize, body.TotalCount));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_PutsTheLowStockFlagInTheQuery(bool lowStock)
    {
        var list = new ListSpy(Result.Success(new PagedResult<InventoryEntryResult>([], 1, 20, 0)));

        await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), CallerId.ToString(), list).List(StoreId, CancellationToken.None, 1, 20, lowStock);

        Assert.Equal(new ListInventoryQuery(StoreId, CallerId, 1, 20, lowStock), list.Received);
    }

    [Fact]
    public async Task List_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var list = new ListSpy(Result.Success(new PagedResult<InventoryEntryResult>([], 1, 20, 0)));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), list: list).List(StoreId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(list.Received);
    }

    [Fact]
    public async Task List_AStoreThatIsNotTheirs_Is404()
    {
        var list = new ListSpy(Result.Failure<PagedResult<InventoryEntryResult>>(GrocerEase.Application.Stores.Common.StoreErrors.NotFound));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), CallerId.ToString(), list).List(StoreId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(null)]
    public async Task SetLowStockThreshold_PutsTheRouteTheCallerAndTheLevelInTheCommand_AndReturnsTheEntry(int? level)
    {
        var threshold = new ThresholdSpy(Result.Success(InventoryEntryResult.FromItem("Milk", new InventoryItem(ProductId, StoreId, 3, level))));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), CallerId.ToString(), threshold: threshold)
            .SetLowStockThreshold(StoreId, ProductId, new SetLowStockThresholdRequest(level), CancellationToken.None);

        Assert.Equal(new SetLowStockThresholdCommand(StoreId, ProductId, CallerId, level), threshold.Received);
        Assert.Equal(new InventoryEntryResponse(ProductId, "Milk", 3, level, level is { } l && 3 <= l), Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task SetLowStockThreshold_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing()
    {
        var threshold = new ThresholdSpy(Result.Success(new InventoryEntryResult(ProductId, "Milk", 3, 5)));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), threshold: threshold)
            .SetLowStockThreshold(StoreId, ProductId, new SetLowStockThresholdRequest(5), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
        Assert.Null(threshold.Received);
    }

    [Theory]
    [InlineData("Store.NotFound")]
    [InlineData("Product.NotFound")]
    public async Task SetLowStockThreshold_WhenTheHandlerFails_Is404(string code)
    {
        var threshold = new ThresholdSpy(Result.Failure<InventoryEntryResult>(new ResultError(code, "x", ErrorType.NotFound)));

        var result = await ControllerWith(new Spy(Result.Success(new StockResult(ProductId, 1))), CallerId.ToString(), threshold: threshold)
            .SetLowStockThreshold(StoreId, ProductId, new SetLowStockThresholdRequest(5), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((StatusCodes.Status404NotFound, code), (problem.Status, problem.Extensions["code"]));
    }

    [Theory]
    [InlineData(nameof(InventoryController.Adjust))]
    [InlineData(nameof(InventoryController.List))]
    [InlineData(nameof(InventoryController.SetLowStockThreshold))]
    public void EachRoute_NeedsThePermissionToManageInventory_NotARoleName(string action)
    {
        var authorize = typeof(InventoryController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.InventoryManage, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void TheMappings_CopyEveryField()
    {
        Assert.Equal(new AdjustStockCommand(StoreId, ProductId, CallerId, 4), new AdjustStockRequest(4).ToCommand(StoreId, ProductId, CallerId));
        Assert.Equal(new StockResponse(ProductId, 9), new StockResult(ProductId, 9).ToResponse());
    }
}
