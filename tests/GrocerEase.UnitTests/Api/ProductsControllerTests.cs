using System.Reflection;
using System.Security.Claims;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Contracts.Products;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class ProductsControllerTests
{
    private sealed class Spy<TCommand, TResponse>(TResponse result) : ICommandHandler<TCommand, TResponse>
    {
        public TCommand? Received { get; private set; }

        public Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly ProductResult Milk = new(Guid.CreateVersion7(), StoreId, "Milk", "Fresh milk", 2.50m);

    private static ProductsController ControllerWith(
        Spy<CreateProductCommand, Result<ProductResult>> create, Spy<GetProductQuery, Result<ProductResult>>? get = null, string? sub = null)
    {
        var controller = new ProductsController(create, get ?? new Spy<GetProductQuery, Result<ProductResult>>(Result.Success(Milk)))
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            sub is null ? [] : [new Claim("sub", sub)], authenticationType: "Bearer"));
        return controller;
    }

    [Fact]
    public async Task Create_PutsTheStoreFromTheRoute_TheCallerFromTheToken_AndTheBodyInTheCommand()
    {
        var spy = new Spy<CreateProductCommand, Result<ProductResult>>(Result.Success(Milk));

        var result = await ControllerWith(spy, sub: CallerId.ToString()).Create(StoreId, new CreateProductRequest("Milk", "Fresh milk", 2.50m), CancellationToken.None);

        Assert.Equal(new CreateProductCommand(StoreId, CallerId, "Milk", "Fresh milk", 2.50m), spy.Received);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ProductsController.Get), created.ActionName);
        Assert.Equal((StoreId, Milk.Id), (created.RouteValues?["storeId"], created.RouteValues?["productId"]));
        Assert.Equal(new ProductResponse(Milk.Id, StoreId, "Milk", "Fresh milk", 2.50m), created.Value);
    }

    [Fact]
    public async Task Create_WithoutADescription_SendsAnEmptyOne()
    {
        var spy = new Spy<CreateProductCommand, Result<ProductResult>>(Result.Success(Milk));

        await ControllerWith(spy, sub: CallerId.ToString()).Create(StoreId, new CreateProductRequest("Milk", null, 1m), CancellationToken.None);

        Assert.Equal(string.Empty, spy.Received?.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-id")]
    public async Task Create_WithATokenThatDoesNotNameAUser_Is401_AndRunsNothing(string? sub)
    {
        var spy = new Spy<CreateProductCommand, Result<ProductResult>>(Result.Success(Milk));
        var controller = ControllerWith(spy, sub: sub);

        var result = await controller.Create(StoreId, new CreateProductRequest("Milk", "", 1m), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((StatusCodes.Status401Unauthorized, "Authentication.InvalidToken"), (problem.Status, problem.Extensions["code"]));
        Assert.Null(spy.Received);
        Assert.Contains("invalid_token", controller.Response.Headers.WWWAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Store.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("Product.DuplicateName", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Create_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var spy = new Spy<CreateProductCommand, Result<ProductResult>>(Result.Failure<ProductResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(spy, sub: CallerId.ToString()).Create(StoreId, new CreateProductRequest("Milk", "", 1m), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task Get_RunsTheQueryWithTheStoreAndTheProductFromTheRoute_AndReturnsIt()
    {
        var get = new Spy<GetProductQuery, Result<ProductResult>>(Result.Success(Milk));
        var create = new Spy<CreateProductCommand, Result<ProductResult>>(Result.Success(Milk));

        var result = await ControllerWith(create, get).Get(StoreId, Milk.Id, CancellationToken.None);

        Assert.Equal(new GetProductQuery(StoreId, Milk.Id), get.Received);
        Assert.Equal(new ProductResponse(Milk.Id, StoreId, "Milk", "Fresh milk", 2.50m), Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Get_AnUnknownProduct_Is404()
    {
        var get = new Spy<GetProductQuery, Result<ProductResult>>(Result.Failure<ProductResult>(ProductErrors.NotFound));

        var result = await ControllerWith(new Spy<CreateProductCommand, Result<ProductResult>>(Result.Success(Milk)), get).Get(StoreId, Milk.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
    }

    [Theory]
    [InlineData(nameof(ProductsController.Create), Permissions.ProductsManage)]
    [InlineData(nameof(ProductsController.Get), Permissions.ProductsRead)]
    public void EachRoute_NeedsItsPermission_NotARoleName(string action, string permission)
    {
        var authorize = typeof(ProductsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(permission, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void GetUserId_ReadsTheSubjectOfTheToken_AndIsNullWhenItIsNotAnId()
    {
        static ClaimsPrincipal Principal(string? sub) =>
            new(new ClaimsIdentity(sub is null ? [] : [new Claim("sub", sub)], authenticationType: "Bearer"));

        Assert.Equal(CallerId, Principal(CallerId.ToString()).GetUserId());
        Assert.Null(Principal("nope").GetUserId());
        Assert.Null(Principal(null).GetUserId());
    }

    [Fact]
    public void TheMappings_CopyEveryField()
    {
        Assert.Equal(new CreateProductCommand(StoreId, CallerId, "A", "B", 1.5m), new CreateProductRequest("A", "B", 1.5m).ToCommand(StoreId, CallerId));
        Assert.Equal(new ProductResponse(Milk.Id, StoreId, "Milk", "Fresh milk", 2.50m), Milk.ToResponse());
    }
}
