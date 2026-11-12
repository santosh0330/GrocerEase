using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Contracts.Stores;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// Who runs a store. An admin assigns a customer to a store, and that is what makes them a store user: there is no separate "change
/// role" call, because the membership and the role are written together or not at all. Guarded by the <c>stores.manage</c> permission,
/// which only the admin holds. It holds no logic: it runs the handler and maps the result.
/// </summary>
/// <remarks>
/// The new store user's access to the store is immediate (every store route asks the database whether the caller is a member), but
/// their token still says <c>Customer</c> until it is refreshed or they sign in again: a role is copied into a token when it is made.
/// </remarks>
[ApiController]
[Route("stores/{storeId:guid}/members")]
[Authorize(Policy = Permissions.StoresManage)]
public sealed class StoreMembersController(
    ICommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>> assignHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    [HttpPost]
    [ProducesResponseType<StoreMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Assign(Guid storeId, AssignStoreMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await assignHandler.Handle(request.ToCommand(storeId), cancellationToken);
        return result.Match<IActionResult>(member => Ok(member.ToResponse()), error => this.ToProblem(error));
    }
}
