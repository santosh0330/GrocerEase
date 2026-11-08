using System.Text;

namespace GrocerEase.Contracts.Authentication;

/// <summary>Body of <c>POST /auth/login</c>.</summary>
public sealed record LoginRequest(
    string Email,
    string Password)
{
    // See RegisterRequest: keep the password out of the generated ToString.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email)
            .Append(", Password = ***");
        return true;
    }
}
