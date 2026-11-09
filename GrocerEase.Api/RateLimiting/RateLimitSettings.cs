namespace GrocerEase.Api.RateLimiting;

/// <summary>
/// How many requests one client may make in a window, for one policy. Each policy has its own section
/// (<c>RateLimiting:Login</c>, <c>RateLimiting:Register</c>) bound to its own named instance of this class,
/// and each is validated when the app starts, so a typo cannot silently turn a limit off or lock everyone out.
/// </summary>
public sealed class RateLimitSettings
{
    public const int MinPermitLimit = 1;
    public const int MaxPermitLimit = 1000;
    public const int MinWindowSeconds = 1;
    public const int MaxWindowSeconds = 3600;

    public int PermitLimit { get; init; }

    public int WindowSeconds { get; init; }
}
