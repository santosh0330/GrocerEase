using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.IntegrationTests;

/// <summary>Makes tokens the way the API does, but lets a test break exactly one thing about them.</summary>
internal static class TestTokens
{
    // The issuer and audience in appsettings.json.
    public const string Issuer = "GrocerEase";
    public const string Audience = "GrocerEase";

    public static string Create(
        Guid? subject = null,
        string key = ApiFactory.JwtSecretKey,
        string issuer = Issuer,
        string audience = Audience,
        TimeSpan? expiresIn = null,
        bool signed = true,
        IDictionary<string, object>? extraClaims = null)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = (subject ?? Guid.CreateVersion7()).ToString() };
        if (extraClaims is not null)
        {
            foreach (var (name, value) in extraClaims)
            {
                claims[name] = value;
            }
        }

        var now = DateTime.UtcNow;
        var lifetime = expiresIn ?? TimeSpan.FromMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = issuer,
            Audience = audience,
            IssuedAt = lifetime < TimeSpan.Zero ? now + lifetime - TimeSpan.FromMinutes(1) : now,
            NotBefore = lifetime < TimeSpan.Zero ? now + lifetime - TimeSpan.FromMinutes(1) : now,
            Expires = now + lifetime,
            SigningCredentials = signed
                ? new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
                : null,
        });
    }
}
