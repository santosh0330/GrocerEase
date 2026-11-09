using System.Security.Claims;
using GrocerEase.Contracts.Users;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.Api.Mapping;

/// <summary>Reads the signed-in user out of the claims of a verified token.</summary>
public static class CurrentUserMappings
{
    /// <summary>
    /// The user a token names, or <c>null</c> when the token does not identify one (a claim missing or blank, or a subject that is
    /// not an id). Such a token is signed, but it was not made by the code that issues user tokens, so the caller is answered
    /// with a 401, not a server error.
    /// </summary>
    public static CurrentUserResponse? ToCurrentUserResponse(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // The claim names the token generator writes. Claim mapping is switched off, so they are not renamed on the way in.
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var firstName = principal.FindFirstValue(JwtRegisteredClaimNames.GivenName);
        var lastName = principal.FindFirstValue(JwtRegisteredClaimNames.FamilyName);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);

        if (!Guid.TryParse(subject, out var id)
            || string.IsNullOrWhiteSpace(firstName)
            || string.IsNullOrWhiteSpace(lastName)
            || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new CurrentUserResponse(id, firstName, lastName, email);
    }
}
