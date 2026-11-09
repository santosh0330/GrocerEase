using GrocerEase.Api.Mapping;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

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
    [HttpPost("register")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registerHandler.Handle(request.ToCommand(), cancellationToken);
        return result.Match<IActionResult>(user => Ok(user.ToResponse()), error => this.ToProblem(error));
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await loginHandler.Handle(request.ToCommand(), cancellationToken);
        return result.Match<IActionResult>(user => Ok(user.ToResponse()), error => this.ToProblem(error));
    }
}
