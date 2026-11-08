using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// Registration and login. Maps the Contracts DTOs to Application commands, runs the handler,
/// and maps the result back to a DTO; it holds no logic of its own.
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(
    ICommandHandler<RegisterCommand, AuthenticationResult> registerHandler,
    ICommandHandler<LoginCommand, AuthenticationResult> loginHandler) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registerHandler.Handle(request.ToCommand(), cancellationToken);
        return Ok(result.ToResponse());
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await loginHandler.Handle(request.ToCommand(), cancellationToken);
        return Ok(result.ToResponse());
    }
}
