using System.Text;

namespace GrocerEase.Application.Authentication.Commands;

/// <summary>Intent to sign in with an email and password.</summary>
public sealed record LoginCommand(
    string Email,
    string Password)
{
    // See RegisterCommand: keep the password out of the generated ToString.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email)
            .Append(", Password = ***");
        return true;
    }
}
