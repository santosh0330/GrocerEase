using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// Issues a signed JWT (HMAC-SHA256). The token proves who the caller is: anyone can read its claims, but only a holder of the
/// signing key can produce or change one, so it carries the user's id and name but never anything secret such as the password hash.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _credentials;

    public JwtTokenGenerator(IOptions<JwtSettings> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = options.Value;
        _timeProvider = timeProvider;
        _credentials = new SigningCredentials(_settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256);
    }

    public string GenerateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            // Registered claim names are used as written ("sub", "email", ...), so no mapping to .NET claim types is involved.
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.GivenName] = user.FirstName,
                [JwtRegisteredClaimNames.FamilyName] = user.LastName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_settings.ExpiryMinutes),
            SigningCredentials = _credentials,
        };

        return _handler.CreateToken(descriptor);
    }
}
