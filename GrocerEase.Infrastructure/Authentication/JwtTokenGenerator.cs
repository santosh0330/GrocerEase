using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// Issues a signed JWT (HMAC-SHA256). The token proves who the caller is: anyone can read its claims, but only a holder of the
/// signing key can produce or change one, so it carries the user's id, name, role and the permissions of that role, but never anything secret such as the password hash.
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

    public string GenerateToken(User user, Role role)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);

        if (user.RoleId != role.Id)
        {
            // A token claims a role for the user, so it must be the role the user holds, never another one handed in by mistake.
            throw new ArgumentException($"The role '{role.Name}' is not the role this user holds.", nameof(role));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Copied into the token, so a request needs no database query to know what its caller may do. The cost is that a change to
        // the role's permissions reaches a caller only with the next token.
        var permissions = role.GrantedPermissions;

        var descriptor = new SecurityTokenDescriptor
        {
            // Registered claim names are used as written ("sub", "email", ...), so no mapping to .NET claim types is involved.
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.GivenName] = user.FirstName,
                [JwtRegisteredClaimNames.FamilyName] = user.LastName,
                [JwtClaims.Role] = role.Name,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_settings.ExpiryMinutes),
            SigningCredentials = _credentials,
        };

        if (permissions.Count > 0)
        {
            // An array, even for one permission, so a reader always finds the same shape. A role that grants nothing gets no claim.
            descriptor.Claims[JwtClaims.Permission] = permissions.ToArray();
        }

        return _handler.CreateToken(descriptor);
    }
}
