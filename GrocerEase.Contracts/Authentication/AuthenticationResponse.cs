using System.Text;

namespace GrocerEase.Contracts.Authentication;

/// <summary>
/// Returned by register, login and refresh: who the user is, the token to send on later requests, <c>expiresAt</c> (UTC) for when that
/// token stops working, so a client knows when to refresh without decoding it, and the refresh token to exchange for a new pair. The refresh token is in the JSON body, so a client stores and sends it itself.
/// </summary>
public sealed record AuthenticationResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Token,
    DateTimeOffset ExpiresAt,
    string RefreshToken)
{
    // Both tokens are credentials: keep them out of the generated ToString.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id)
            .Append(", FirstName = ").Append(FirstName)
            .Append(", LastName = ").Append(LastName)
            .Append(", Email = ").Append(Email)
            .Append(", Token = ***")
            .Append(", ExpiresAt = ").Append(ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .Append(", RefreshToken = ***");
        return true;
    }
}
