using System.Reflection;
using GrocerEase.Api.Controllers;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Roles.Common;
using GrocerEase.Application.Roles.Queries;
using GrocerEase.Contracts.Roles;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class RolesControllerTests
{
    private sealed class SpyHandler(Result<IReadOnlyList<RoleResult>> result) : ICommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>>
    {
        public ListRolesQuery? ReceivedQuery { get; private set; }

        public Task<Result<IReadOnlyList<RoleResult>>> Handle(ListRolesQuery command, CancellationToken cancellationToken)
        {
            ReceivedQuery = command;
            return Task.FromResult(result);
        }
    }

    private static Result<IReadOnlyList<RoleResult>> Roles() =>
        Result.Success<IReadOnlyList<RoleResult>>(
        [
            new(RoleIds.Admin, RoleNames.Admin, [Permissions.ProfileRead, Permissions.RolesRead]),
            new(RoleIds.Customer, RoleNames.Customer, [Permissions.ProfileRead]),
        ]);

    private static RolesController ControllerWith(SpyHandler handler) => new(handler) { ProblemDetailsFactory = new StubProblemDetailsFactory() };

    [Fact]
    public async Task List_RunsTheQuery_AndReturnsEveryRoleMappedToAResponse()
    {
        var handler = new SpyHandler(Roles());

        var result = await ControllerWith(handler).List(CancellationToken.None);

        Assert.NotNull(handler.ReceivedQuery);
        var body = Assert.IsAssignableFrom<IEnumerable<RoleResponse>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        Assert.Equal(
            [(RoleIds.Admin, RoleNames.Admin), (RoleIds.Customer, RoleNames.Customer)],
            body.Select(role => (role.Id, role.Name)));
        Assert.Equal([Permissions.ProfileRead, Permissions.RolesRead], body[0].Permissions, StringComparer.Ordinal);
        Assert.Equal([Permissions.ProfileRead], body[1].Permissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task List_WhenTheHandlerFails_ReturnsAProblemForThatError()
    {
        var error = new ResultError("Role.Broken", "Something is wrong.", ErrorType.NotFound);
        var handler = new SpyHandler(Result.Failure<IReadOnlyList<RoleResult>>(error));

        var result = await ControllerWith(handler).List(CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Role.Broken", problem.Extensions["code"]);
    }

    [Fact]
    public void TheController_NeedsThePermissionToReadRoles_NotARoleName()
    {
        var authorize = typeof(RolesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.RolesRead, authorize?.Policy);
        Assert.Null(authorize?.Roles);
    }

    [Fact]
    public void ToResponse_CopiesTheIdTheNameAndThePermissions()
    {
        var response = new RoleResult(RoleIds.Customer, "Customer", [Permissions.ProfileRead]).ToResponse();

        Assert.Equal((RoleIds.Customer, "Customer"), (response.Id, response.Name));
        Assert.Equal([Permissions.ProfileRead], response.Permissions, StringComparer.Ordinal);
    }
}
