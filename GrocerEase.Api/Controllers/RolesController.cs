using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Roles.Common;
using GrocerEase.Application.Roles.Queries;
using GrocerEase.Contracts.Roles;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The roles the application knows. Whoever may read them is whoever holds the <c>roles.read</c> permission: the policy checks the
/// <c>permission</c> claims of the token, not a role name, so a signed-in user without it is answered with a 403 and never reaches
/// this code. It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
[Route("roles")]
[Authorize(Policy = Permissions.RolesRead)]
public sealed class RolesController(ICommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>> listHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await listHandler.Handle(new ListRolesQuery(), cancellationToken);
        return result.Match<IActionResult>(
            roles => Ok(roles.Select(role => role.ToResponse())),
            error => this.ToProblem(error));
    }
}
