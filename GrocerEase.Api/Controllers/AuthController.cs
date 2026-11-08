using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>Registration and login. Request and response bodies are the DTOs from the Contracts layer.</summary>
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    // TEMPORARY: no use-case handlers exist yet, so both actions echo the input with a placeholder
    // token. Replace the bodies with calls to the Application layer; do not deploy as is.
    private const string PlaceholderToken = "placeholder-token";

    [HttpPost("register")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public IActionResult Register(RegisterRequest request) =>
        Ok(new AuthenticationResponse(Guid.NewGuid(), request.FirstName, request.LastName, request.Email, PlaceholderToken));

    [HttpPost("login")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public IActionResult Login(LoginRequest request) =>
        Ok(new AuthenticationResponse(Guid.NewGuid(), "placeholder", "placeholder", request.Email, PlaceholderToken));
}
