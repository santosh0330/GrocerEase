using System.Reflection;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Stores;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class StoresControllerTests
{
    private sealed class SpyHandler<TCommand, TResponse>(TResponse result) : ICommandHandler<TCommand, TResponse>
    {
        public TCommand? Received { get; private set; }

        public Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly StoreResult FreshMart = new(Guid.CreateVersion7(), "Fresh Mart", "1 High Street");

    private static StoresController ControllerWith(
        Result<StoreResult>? create = null, Result<StoreResult>? get = null, Result<PagedResult<StoreResult>>? list = null) =>
        new(
            new SpyHandler<CreateStoreCommand, Result<StoreResult>>(create ?? Result.Success(FreshMart)),
            new SpyHandler<GetStoreQuery, Result<StoreResult>>(get ?? Result.Success(FreshMart)),
            new SpyHandler<ListStoresQuery, Result<PagedResult<StoreResult>>>(list ?? Result.Success(new PagedResult<StoreResult>([FreshMart], 1, 20, 1))))
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
        };

    [Fact]
    public async Task Create_ReturnsCreated_WithTheStoreAndWhereToFindIt()
    {
        var spy = new SpyHandler<CreateStoreCommand, Result<StoreResult>>(Result.Success(FreshMart));
        var controller = new StoresController(
            spy,
            new SpyHandler<GetStoreQuery, Result<StoreResult>>(Result.Success(FreshMart)),
            new SpyHandler<ListStoresQuery, Result<PagedResult<StoreResult>>>(Result.Success(new PagedResult<StoreResult>([], 1, 20, 0))))
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
        };

        var result = await controller.Create(new CreateStoreRequest("Fresh Mart", "1 High Street"), CancellationToken.None);

        Assert.Equal(new CreateStoreCommand("Fresh Mart", "1 High Street"), spy.Received);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(StoresController.Get), created.ActionName);
        Assert.Equal(FreshMart.Id, created.RouteValues?["storeId"]);
        Assert.Equal(new StoreResponse(FreshMart.Id, "Fresh Mart", "1 High Street"), created.Value);
    }

    [Fact]
    public async Task Create_WhenTheNameIsTaken_ReturnsA409Problem()
    {
        var result = await ControllerWith(create: Result.Failure<StoreResult>(StoreErrors.DuplicateName))
            .Create(new CreateStoreRequest("Fresh Mart", "1 High Street"), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Store.DuplicateName", problem.Extensions["code"]);
    }

    [Fact]
    public async Task Get_ReturnsTheStore() =>
        Assert.Equal(
            new StoreResponse(FreshMart.Id, "Fresh Mart", "1 High Street"),
            Assert.IsType<OkObjectResult>(await ControllerWith().Get(FreshMart.Id, CancellationToken.None)).Value);

    [Fact]
    public async Task Get_AnUnknownStore_ReturnsA404Problem()
    {
        var result = await ControllerWith(get: Result.Failure<StoreResult>(StoreErrors.NotFound)).Get(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);
    }

    [Fact]
    public async Task List_ReturnsThePageMappedToAResponse_WithItsPaging()
    {
        var result = await ControllerWith(list: Result.Success(new PagedResult<StoreResult>([FreshMart], 3, 5, 11))).List(CancellationToken.None, page: 3, pageSize: 5);

        var body = Assert.IsType<PagedResponse<StoreResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([new StoreResponse(FreshMart.Id, "Fresh Mart", "1 High Street")], body.Items);
        Assert.Equal((3, 5, 11), (body.Page, body.PageSize, body.TotalCount));
    }

    [Fact]
    public async Task List_WithNoPaging_AsksForTheFirstPageOfTheDefaultSize()
    {
        var spy = new SpyHandler<ListStoresQuery, Result<PagedResult<StoreResult>>>(Result.Success(new PagedResult<StoreResult>([], 1, 20, 0)));
        var controller = new StoresController(
            new SpyHandler<CreateStoreCommand, Result<StoreResult>>(Result.Success(FreshMart)),
            new SpyHandler<GetStoreQuery, Result<StoreResult>>(Result.Success(FreshMart)),
            spy)
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
        };

        await controller.List(CancellationToken.None);

        Assert.Equal(new ListStoresQuery(1, Paging.DefaultPageSize), spy.Received);
    }

    [Theory]
    [InlineData(nameof(StoresController.Create), Permissions.StoresManage)]
    [InlineData(nameof(StoresController.List), Permissions.StoresRead)]
    [InlineData(nameof(StoresController.Get), Permissions.StoresRead)]
    public void EachRoute_NeedsItsPermission_NotARoleName(string action, string permission)
    {
        var authorize = typeof(StoresController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(permission, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void TheMappings_CopyEveryField()
    {
        Assert.Equal(new CreateStoreCommand("A", "B"), new CreateStoreRequest("A", "B").ToCommand());
        Assert.Equal(new StoreResponse(FreshMart.Id, "Fresh Mart", "1 High Street"), FreshMart.ToResponse());
    }
}
