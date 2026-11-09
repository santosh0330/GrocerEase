using System.ComponentModel.DataAnnotations;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// The <c>Jwt</c> section of the configuration. Everything except the key lives in <c>appsettings.json</c>;
/// the key is a secret and comes from user-secrets or the environment (see the README). It is validated when the app starts,
/// so a missing or weak key stops the app at once instead of failing on the first login.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    // HS256 signs with a 256-bit key: 32 characters is the least that can carry that much.
    public const int MinimumSecretKeyLength = 32;

    [Required(ErrorMessage = "Jwt:SecretKey is required. Set it with user-secrets or JWT_SECRET_KEY in .env (see the README).")]
    [MinLength(MinimumSecretKeyLength, ErrorMessage = "Jwt:SecretKey must be at least 32 characters.")]
    public string SecretKey { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Issuer is required.")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience is required.")]
    public string Audience { get; init; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "Jwt:ExpiryMinutes must be between 1 and 1440 (one day).")]
    public int ExpiryMinutes { get; init; }
}
