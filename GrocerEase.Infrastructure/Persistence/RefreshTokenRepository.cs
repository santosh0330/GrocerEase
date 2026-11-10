using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Stores refresh tokens in PostgreSQL through EF Core. Scoped, like the context it wraps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why there is no race.</b> The change of state is one SQL statement with the condition inside it:
/// <c>UPDATE ... SET consumed_at = @now WHERE id = @id AND consumed_at IS NULL AND revoked_at IS NULL</c>. Reading the token
/// and then updating it would leave a gap in which two requests both see it unused. In one statement, the second request waits
/// for the first one's row lock, then re-checks the condition on the new row, finds it consumed, and changes nothing.
/// </para>
/// <para>
/// A second hazard is a revocation that misses a token being created at that very moment: the revoking statement only sees rows
/// that existed when it started, so a new token committed a moment later would stay live. Every operation on one family
/// therefore first takes a transaction-scoped advisory lock keyed by the family. Operations on the same family run one after
/// another; operations on different families do not wait for each other.
/// </para>
/// <para>
/// Nothing here logs or returns a token: the store only ever holds the hash, and the logs only ever carry ids.
/// </para>
/// </remarks>
public sealed partial class RefreshTokenRepository(AppDbContext db, ILogger<RefreshTokenRepository> logger) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(token).State = EntityState.Detached; // handed over to the store: the context has no reason to remember it
    }

    public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        // No tracking: the state of a token is changed by statements in the store, never through the context.
        return db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<bool> TryRotateAsync(
        RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (current.FamilyId != replacement.FamilyId)
        {
            throw new ArgumentException("The replacement must belong to the same family as the token it replaces.", nameof(replacement));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(current.FamilyId, cancellationToken);

        var consumed = await db.RefreshTokens
            .Where(token => token.Id == current.Id && token.ConsumedAt == null && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return false; // the transaction is disposed without a commit: nothing changed
        }

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.Entry(replacement).State = EntityState.Detached;
        return true;
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId, cancellationToken);

        var revoked = await db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogFamilyRevoked(familyId, revoked);
    }

    // Held until the transaction ends. hashtextextended turns the family id into the bigint an advisory lock takes.
    private Task<int> LockFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({familyId.ToString()}, 0))", cancellationToken);

    // Ids and a count only: never a token, and not its hash either.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoked refresh token family {FamilyId} ({RevokedCount} tokens)")]
    private partial void LogFamilyRevoked(Guid familyId, int revokedCount);
}
