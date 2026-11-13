using System.Text;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>What the register, login and refresh handlers return. <c>ExpiresAt</c> is when the access token stops working, as the token generator said. The Api layer maps it to its response DTO.</summary>
public sealed record AuthenticationResult(
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
