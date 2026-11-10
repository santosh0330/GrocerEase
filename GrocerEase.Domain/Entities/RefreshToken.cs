namespace GrocerEase.Domain.Entities;

/// <summary>
/// The server's record of one refresh token handed to a client. The token itself (a random string) is never stored: only its
/// SHA-256 hash is, so a copy of the database cannot be used to sign in. The entity guards its own rules, like <see cref="User"/>.
/// </summary>
/// <remarks>
/// Every sign-in starts a <b>family</b>. Each refresh <i>replaces</i> the token with a new one in the same family, and the old
/// one is marked as consumed. So at most one token of a family is live at a time, and a consumed token showing up again means
/// somebody kept a copy: the whole family is then revoked. The family also has a hard end (<see cref="FamilyExpiresAt"/>), so a
/// session cannot be kept alive by refreshing forever.
/// </remarks>
public sealed class RefreshToken
{
    public const int RequiredIdVersion = 7;

    /// <summary>The length of a SHA-256 hash in bytes.</summary>
    public const int HashLength = 32;

    /// <summary>The most days any refresh token, and any family of them, may live. One definition: settings, entity and table all follow it.</summary>
    public const int MaxLifetimeDays = 30;

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(MaxLifetimeDays);

    public RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset familyExpiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(familyId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != HashLength)
        {
            throw new ArgumentException($"The hash must be {HashLength} bytes (SHA-256).", nameof(tokenHash));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("The token must expire after it is created.", nameof(expiresAt));
        }

        if (expiresAt - createdAt > MaxLifetime)
        {
            throw new ArgumentException($"A refresh token may live at most {MaxLifetimeDays} days.", nameof(expiresAt));
        }

        if (expiresAt > familyExpiresAt)
        {
            throw new ArgumentException("The token may not outlive its family.", nameof(expiresAt));
        }

        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = [.. tokenHash]; // a copy, so the caller's array cannot change what is stored
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        FamilyExpiresAt = familyExpiresAt;
    }

    // For EF Core, which fills the properties itself when it reads a row.
    private RefreshToken()
    {
        TokenHash = [];
    }

    /// <summary>
    /// The first token of a new family, issued at sign-in. It lives for <paramref name="lifetime"/>, and the family for at most
    /// <paramref name="maxLifetime"/> (never more than <see cref="MaxLifetime"/>), counted from <paramref name="now"/>.
    /// </summary>
    public static RefreshToken Start(Guid userId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime, TimeSpan maxLifetime)
    {
        ThrowIfNotPositive(lifetime, nameof(lifetime));
        ThrowIfNotPositive(maxLifetime, nameof(maxLifetime));
        if (maxLifetime > MaxLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLifetime), maxLifetime, $"A family may live at most {MaxLifetimeDays} days.");
        }

        var familyExpiresAt = now + maxLifetime;
        return new RefreshToken(
            Guid.CreateVersion7(), userId, Guid.CreateVersion7(), tokenHash, now, Earlier(now + lifetime, familyExpiresAt), familyExpiresAt);
    }

    /// <summary>
    /// The token that replaces this one when it is refreshed: same user, same family, same hard end. It lives for
    /// <paramref name="lifetime"/> from <paramref name="now"/>, but never past the end of the family, so each refresh gets a fresh
    /// window until the family's last day and no longer.
    /// </summary>
    public RefreshToken Successor(byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        ThrowIfNotPositive(lifetime, nameof(lifetime));

        return new RefreshToken(
            Guid.CreateVersion7(), UserId, FamilyId, tokenHash, now, Earlier(now + lifetime, FamilyExpiresAt), FamilyExpiresAt);
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    /// <summary>Shared by every token that descends from the same sign-in. Revoking a family ends that whole session.</summary>
    public Guid FamilyId { get; }

    /// <summary>SHA-256 of the token string. The token itself is never kept.</summary>
    public byte[] TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>When the family ends, whatever happens: the upper bound on a session.</summary>
    public DateTimeOffset FamilyExpiresAt { get; }

    /// <summary>When this token was exchanged for its successor. Set by the store, in the same step that checks it was still unused.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>When this token was revoked because the family's session was ended. Set by the store.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// True once the token has been exchanged or revoked. Presenting a spent token again is the sign that a copy exists
    /// somewhere it should not.
    /// </summary>
    public bool IsSpent => ConsumedAt is not null || RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;

    private static void ThrowIfNotPositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The duration must be positive.");
        }
    }
}
