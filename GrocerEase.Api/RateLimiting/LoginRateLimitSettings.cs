using System.ComponentModel.DataAnnotations;

namespace GrocerEase.Api.RateLimiting;

/// <summary>
/// The <c>RateLimiting:Login</c> section: how many login attempts one client may make in a window.
/// Validated when the app starts, so a typo cannot silently turn the protection off (or lock everyone out).
/// </summary>
public sealed class LoginRateLimitSettings
{
    public const string SectionName = "RateLimiting:Login";

    [Range(1, 1000, ErrorMessage = "RateLimiting:Login:PermitLimit must be between 1 and 1000.")]
    public int PermitLimit { get; init; }

    [Range(1, 3600, ErrorMessage = "RateLimiting:Login:WindowSeconds must be between 1 and 3600 (one hour).")]
    public int WindowSeconds { get; init; }
}
