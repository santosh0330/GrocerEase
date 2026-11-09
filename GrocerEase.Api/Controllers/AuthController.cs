using GrocerEase.Api.Mapping;
using GrocerEase.Api.RateLimiting;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// Registration and login. Maps the Contracts DTOs to Application commands, runs the handler,
/// and maps its result back: a response DTO on success, a problem on failure. It holds no logic of its own.
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(
    ICommandHandler<RegisterCommand, Result<AuthenticationResult>> registerHandler,
    ICommandHandler<LoginCommand, Result<AuthenticationResult>> loginHandler) : ControllerBase
{
    // Failures are RFC 9457 problem documents, so say so: without it the OpenAPI document lists the default text/json types.
    private const string ProblemJson = "application/problem+json";

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingExtensions.RegisterPolicy)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registerHandler.Handle(request.ToCommand(), cancellationToken);
        return result.Match<IActionResult>(user => Ok(user.ToResponse()), error => this.ToProblem(error));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingExtensions.LoginPolicy)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemJson)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await loginHandler.Handle(request.ToCommand(), cancellationToken);
        return result.Match<IActionResult>(user => Ok(user.ToResponse()), error => this.ToProblem(error));
    }
}
