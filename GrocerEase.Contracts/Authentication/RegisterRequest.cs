using System.Text;

namespace GrocerEase.Contracts.Authentication;

/// <summary>Body of <c>POST /auth/register</c>.</summary>
public sealed record RegisterRequest(
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
