using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Products;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The products of a store. Anyone with <c>products.read</c> may browse them; adding, editing and archiving need
/// <c>products.manage</c>, which only a store user holds, <b>and</b> being one of the people who run <i>that</i> store. The permission
/// says what kind of user may do it; the store is checked against the database by the use case, and a store the caller does not run
/// is a 404, never a 403. It holds no logic: it runs the handler and maps the result.
/// </summary>
[ApiController]
[Route("stores/{storeId:guid}/products")]
public sealed class ProductsController(
    ICommandHandler<CreateProductCommand, Result<ProductResult>> createHandler,
    ICommandHandler<GetProductQuery, Result<ProductResult>> getHandler,
    ICommandHandler<UpdateProductCommand, Result<ProductResult>> updateHandler,
    ICommandHandler<ListProductsQuery, Result<PagedResult<ProductResult>>> listHandler) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    [HttpPost]
    [Authorize(Policy = Permissions.ProductsManage)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Create(Guid storeId, CreateProductRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await createHandler.Handle(request.ToCommand(storeId, callerId), cancellationToken);
        return result.Match<IActionResult>(
            product => CreatedAtAction(nameof(Get), new { storeId, productId = product.Id }, product.ToResponse()),
            error => this.ToProblem(error));
    }

    // page counts from 1. The limits are the same for every list (Paging): a value outside them is a 400 naming the parameter.
    [HttpGet]
    [Authorize(Policy = Permissions.ProductsRead)]
    [ProducesResponseType<PagedResponse<ProductResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> List(
        Guid storeId, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        var result = await listHandler.Handle(new ListProductsQuery(storeId, page, pageSize), cancellationToken);
        return result.Match<IActionResult>(products => Ok(products.ToResponse()), error => this.ToProblem(error));
    }

    // Replaces the name, description and price. The id and the store do not change.
    [HttpPut("{productId:guid}")]
    [Authorize(Policy = Permissions.ProductsManage)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Update(Guid storeId, Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } callerId)
        {
            return this.InvalidTokenProblem();
        }

        var result = await updateHandler.Handle(request.ToCommand(storeId, productId, callerId), cancellationToken);
        return result.Match<IActionResult>(product => Ok(product.ToResponse()), error => this.ToProblem(error));
    }

    [HttpGet("{productId:guid}")]
    [Authorize(Policy = Permissions.ProductsRead)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Get(Guid storeId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await getHandler.Handle(new GetProductQuery(storeId, productId), cancellationToken);
        return result.Match<IActionResult>(product => Ok(product.ToResponse()), error => this.ToProblem(error));
    }
}
