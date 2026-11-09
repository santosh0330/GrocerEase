using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Three routes that exist only in the test host, so the authentication mechanism is tested on its own, without leaning on a
/// real endpoint: one that needs a signed-in user, one that needs an "admin" claim (so the 403 path can be reached), one open.
/// </summary>
[ApiController]
[Route("test-only")]
public sealed class AuthProbeController : ControllerBase
{
    public const string AdminPolicy = "TestOnlyAdmin";

    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult Authenticated() => Ok(new { subject = User.FindFirstValue("sub") });

    [HttpGet("admin")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult Admin() => Ok();

    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok();
}
