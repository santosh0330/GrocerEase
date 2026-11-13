using System.Security.Claims;
using GrocerEase.Api.Authentication;
using GrocerEase.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.Api.Mapping;

/// <summary>
/// Who is calling, for the routes whose use case needs to know (the ownership rule). Application never touches HTTP, so the controller
/// reads the caller's id out of the verified token and puts it in the command.
/// </summary>
public static class CallerMappings
{
    /// <summary>
    /// The id of the user a token names, or <c>null</c> when the subject is missing or is not an id. Such a token is signed, but it was
    /// not made by the code that issues user tokens, so the caller is answered with a 401.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
    }

    /// <summary>The same 401 as <c>GET /me</c> gives for a token that does not identify a user.</summary>
    public static IActionResult InvalidTokenProblem(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        controller.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return controller.ToProblem(new ResultError(
            AuthenticationExtensions.InvalidTokenCode, "The token does not identify a user.", ErrorType.Unauthorized));
    }
}
