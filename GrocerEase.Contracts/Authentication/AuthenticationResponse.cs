using System.Text;

namespace GrocerEase.Contracts.Authentication;

/// <summary>Returned by register and login: who the user is, plus the token to send on later requests.</summary>
public sealed record AuthenticationResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Token)
{
    // The token is a credential: keep it out of the generated ToString.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id)
            .Append(", FirstName = ").Append(FirstName)
            .Append(", LastName = ").Append(LastName)
            .Append(", Email = ").Append(Email)
            .Append(", Token = ***");
        return true;
    }
}
