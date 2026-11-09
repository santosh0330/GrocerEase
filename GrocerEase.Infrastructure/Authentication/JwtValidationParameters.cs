using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// What counts as a valid token. Built from the same <see cref="JwtSettings"/> that <see cref="JwtTokenGenerator"/> signs with,
/// so the key, the issuer and the audience cannot differ between the side that makes a token and the side that checks it.
/// </summary>
public static class JwtValidationParameters
{
    /// <summary>
    /// How far a token's lifetime may be off, to forgive two servers whose clocks differ a little. The library's default is five
    /// minutes, which would quietly make every token live five minutes longer than the 60 we say it does.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static TokenValidationParameters From(JwtSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new TokenValidationParameters
        {
            RequireSignedTokens = true, // a token that says "I need no signature" is refused
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = settings.CreateSigningKey(),

            // Only the algorithm we sign with. Without this, any algorithm the key could be used with would be accepted.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,

            ValidateAudience = true,
            ValidAudience = settings.Audience,

            ValidateLifetime = true,
            RequireExpirationTime = true, // a token that never expires is refused
            ClockSkew = ClockSkew,
        };
    }
}
