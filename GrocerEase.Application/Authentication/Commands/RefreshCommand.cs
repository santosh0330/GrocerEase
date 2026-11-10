using System.Text;

namespace GrocerEase.Application.Authentication.Commands;

/// <summary>Intent to exchange a refresh token for a new access token and a new refresh token. Application's own type.</summary>
public sealed record RefreshCommand(string RefreshToken)
{
    // The refresh token is a credential: keep it out of the generated ToString, so no log line can ever carry it.
    // It says only whether one was supplied, never what it was.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(RefreshToken is null ? "null" : "***");
        return true;
    }
}
