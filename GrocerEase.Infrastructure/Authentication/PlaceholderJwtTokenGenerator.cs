using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// TEMPORARY: returns a fixed string, not a signed JWT, so anyone could "hold" a valid token.
/// Replace with a generator that signs a token with a secret key; do not deploy as is.
/// </summary>
public sealed class PlaceholderJwtTokenGenerator : IJwtTokenGenerator
{
    public string GenerateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return "placeholder-token";
    }
}
