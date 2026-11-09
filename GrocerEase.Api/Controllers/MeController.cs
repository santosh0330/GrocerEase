using GrocerEase.Api.Authentication;
using GrocerEase.Api.Mapping;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Controllers;

/// <summary>
/// The first route only a signed-in user may reach. <c>[Authorize]</c> makes the authentication middleware demand a valid bearer
/// token before this code runs; without one the caller gets a 401 and never gets here. It holds no logic: it reads the identity
/// from the verified token and maps it to a response.
/// </summary>
[ApiController]
[Route("me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    [HttpGet]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson)]
    public IActionResult Get()
    {
        var me = User.ToCurrentUserResponse();
        if (me is null)
        {
            // A token that is validly signed but does not identify a user: refuse it like any other bad token.
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return this.ToProblem(new ResultError(
                AuthenticationExtensions.InvalidTokenCode, "The token does not identify a user.", ErrorType.Unauthorized));
        }

        return Ok(me);
    }
}
