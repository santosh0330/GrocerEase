using System.Text;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>What the register and login handlers return. The Api layer maps it to its response DTO.</summary>
public sealed record AuthenticationResult(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Token,
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
            .Append(", RefreshToken = ***");
        return true;
    }
}
