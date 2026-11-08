using System.Text;

namespace GrocerEase.Application.Authentication.Commands;

/// <summary>Intent to create a new user account. Application's own type: it must not reference Contracts.</summary>
public sealed record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password)
{
    // A record's generated ToString prints every property, which would put the password in logs.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("FirstName = ").Append(FirstName)
            .Append(", LastName = ").Append(LastName)
            .Append(", Email = ").Append(Email)
            .Append(", Password = ***");
        return true;
    }
}
