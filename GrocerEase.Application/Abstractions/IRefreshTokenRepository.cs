using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where refresh tokens are stored: by hash, never the token itself. Application owns the interface and Infrastructure implements it.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Stores the first token of a new family, issued at sign-in.</summary>
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>Finds a token by the hash of what the client sent, whatever state it is in. Returns <c>null</c> when there is none.</summary>
    Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges <paramref name="current"/> for <paramref name="replacement"/> as one indivisible step: marks the current token
    /// consumed, only if it is still unused and not revoked, and stores the replacement. Returns <c>false</c>, and changes nothing,
    /// when the token was already consumed or revoked. When two callers present the same token at once, exactly one gets
    /// <c>true</c>: the check and the change are one statement in the store, so there is no gap between them to race in.
    /// </summary>
    Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every token of the family, ending the session. Safe to call twice. A rotation of the same family that is running
    /// at the same moment either finishes first (and its new token is revoked too) or is refused: no token of the family stays live.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);
}
