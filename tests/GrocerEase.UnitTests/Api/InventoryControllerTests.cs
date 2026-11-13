using System.Reflection;
using System.Security.Claims;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
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

    private static InventoryController ControllerWith(Spy adjust, string? sub = null)
    {
        var controller = new InventoryController(adjust)
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
    public void TheRoute_NeedsThePermissionToManageInventory_NotARoleName()
    {
        var authorize = typeof(InventoryController).GetMethod(nameof(InventoryController.Adjust))!.GetCustomAttribute<AuthorizeAttribute>();

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
