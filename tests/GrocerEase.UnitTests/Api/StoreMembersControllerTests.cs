using System.Reflection;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Contracts.Stores;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class StoreMembersControllerTests
{
    private sealed class SpyHandler(Result<StoreMemberResult> result) : ICommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>>
    {
        public AssignStoreMemberCommand? Received { get; private set; }

        public Task<Result<StoreMemberResult>> Handle(AssignStoreMemberCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static readonly StoreMemberResult Asha = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Asha", "Rai", "asha@example.com");

    private sealed class SpyRemoveHandler(Result<StoreMemberResult> result) : ICommandHandler<RemoveStoreMemberCommand, Result<StoreMemberResult>>
    {
        public RemoveStoreMemberCommand? Received { get; private set; }

        public Task<Result<StoreMemberResult>> Handle(RemoveStoreMemberCommand command, CancellationToken cancellationToken)
        {
            Received = command;
            return Task.FromResult(result);
        }
    }

    private static StoreMembersController ControllerWith(SpyHandler handler, SpyRemoveHandler? remove = null) =>
        new(handler, remove ?? new SpyRemoveHandler(Result.Success(Asha))) { ProblemDetailsFactory = new StubProblemDetailsFactory() };

    [Fact]
    public async Task Remove_PutsTheStoreAndTheUserFromTheRouteInTheCommand_AndReturnsNoContent()
    {
        var spy = new SpyRemoveHandler(Result.Success(Asha));

        var result = await ControllerWith(new SpyHandler(Result.Success(Asha)), spy).Remove(Asha.StoreId, Asha.UserId, CancellationToken.None);

        Assert.Equal(new RemoveStoreMemberCommand(Asha.StoreId, Asha.UserId), spy.Received);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_WhenTheHandlerFails_ReturnsAProblemForThatError()
    {
        var spy = new SpyRemoveHandler(Result.Failure<StoreMemberResult>(StoreErrors.MemberNotFound));

        var result = await ControllerWith(new SpyHandler(Result.Success(Asha)), spy).Remove(Asha.StoreId, Asha.UserId, CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((StatusCodes.Status404NotFound, "Store.MemberNotFound"), (problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task Assign_PutsTheStoreFromTheRouteAndTheEmailFromTheBodyInTheCommand_AndReturnsTheMember()
    {
        var spy = new SpyHandler(Result.Success(Asha));

        var result = await ControllerWith(spy).Assign(Asha.StoreId, new AssignStoreMemberRequest("asha@example.com"), CancellationToken.None);

        Assert.Equal(new AssignStoreMemberCommand(Asha.StoreId, "asha@example.com"), spy.Received);
        Assert.Equal(
            new StoreMemberResponse(Asha.StoreId, Asha.UserId, "Asha", "Rai", "asha@example.com"),
            Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("Store.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("User.NotFound", ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData("User.NotACustomer", ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public async Task Assign_WhenTheHandlerFails_ReturnsAProblemForThatError(string code, ErrorType type, int status)
    {
        var spy = new SpyHandler(Result.Failure<StoreMemberResult>(new ResultError(code, "x", type)));

        var result = await ControllerWith(spy).Assign(Asha.StoreId, new AssignStoreMemberRequest("asha@example.com"), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal((status, code), (problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public void TheController_NeedsThePermissionToManageStores_NotARoleName()
    {
        var authorize = typeof(StoreMembersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.StoresManage, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void TheResponse_NeverCarriesAPasswordHash() =>
        Assert.DoesNotContain(
            typeof(StoreMemberResponse).GetProperties(), property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
}
