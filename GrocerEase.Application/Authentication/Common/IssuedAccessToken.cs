using System.Text;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>
/// An access token and the moment it stops working, handed back together by the one place that decides both. <paramref name="ExpiresAt"/> is
/// the same instant the token carries as its <c>exp</c> claim (to the second, which is all a JWT can say), so whoever tells a client when to
/// refresh cannot be off by a rounding or by a second setting.
/// </summary>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt)
{
    // The token is a credential: keep it out of the generated ToString.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ***").Append(", ExpiresAt = ").Append(ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}
