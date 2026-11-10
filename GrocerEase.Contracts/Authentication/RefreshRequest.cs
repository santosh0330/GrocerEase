using System.Text;

namespace GrocerEase.Contracts.Authentication;

/// <summary>Body of <c>POST /auth/refresh</c>: the refresh token the client received at sign-in or at its last refresh.</summary>
public sealed record RefreshRequest(string RefreshToken)
{
    // The refresh token is a credential: keep it out of the generated ToString. It says only whether one was supplied.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(RefreshToken is null ? "null" : "***");
        return true;
    }
}
