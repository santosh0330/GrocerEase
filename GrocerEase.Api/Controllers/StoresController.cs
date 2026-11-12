using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;
using GrocerEase.Contracts.Stores;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The stores. Anyone with <c>stores.read</c> may list and read them; creating one needs <c>stores.manage</c>, which only the admin
/// holds. The policy checks the <c>permission</c> claims of the token, not a role name, so a signed-in user without the permission is
/// answered with a 403 and never reaches this code. It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
[Route("stores")]
public sealed class StoresController(
    ICommandHandler<CreateStoreCommand, Result<StoreResult>> createHandler,
    ICommandHandler<GetStoreQuery, Result<StoreResult>> getHandler,
    ICommandHandler<ListStoresQuery, Result<IReadOnlyList<StoreResult>>> listHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    [HttpPost]
    [Authorize(Policy = Permissions.StoresManage)]
    [ProducesResponseType<StoreResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Create(CreateStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await createHandler.Handle(request.ToCommand(), cancellationToken);
        return result.Match<IActionResult>(
            store => CreatedAtAction(nameof(Get), new { storeId = store.Id }, store.ToResponse()),
            error => this.ToProblem(error));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.StoresRead)]
    [ProducesResponseType<IReadOnlyList<StoreResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await listHandler.Handle(new ListStoresQuery(), cancellationToken);
        return result.Match<IActionResult>(
            stores => Ok(stores.Select(store => store.ToResponse())),
            error => this.ToProblem(error));
    }

    [HttpGet("{storeId:guid}")]
    [Authorize(Policy = Permissions.StoresRead)]
    [ProducesResponseType<StoreResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Get(Guid storeId, CancellationToken cancellationToken)
    {
        var result = await getHandler.Handle(new GetStoreQuery(storeId), cancellationToken);
        return result.Match<IActionResult>(store => Ok(store.ToResponse()), error => this.ToProblem(error));
    }
}
