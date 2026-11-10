using System.Reflection;
using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// A list standing in for the table in handler tests. It follows the port's contract: a rotation succeeds only for a token that is
/// still unused, and only one of two callers presenting the same token wins. The state a real store sets with SQL
/// (<c>ConsumedAt</c>, <c>RevokedAt</c>) is set here by reflection, the way EF Core sets it when it reads a row.
/// </summary>
internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Lock _gate = new();
    private readonly List<RefreshToken> _tokens = [];
    private readonly List<Guid> _revokedFamilies = [];

    public IReadOnlyList<RefreshToken> Tokens
    {
        get
        {
            lock (_gate)
            {
                return [.. _tokens];
            }
        }
    }

    public IReadOnlyList<Guid> RevokedFamilies
    {
        get
        {
            lock (_gate)
            {
                return [.. _revokedFamilies];
            }
        }
    }

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            _tokens.Add(token);
        }

        return Task.CompletedTask;
    }

    public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        lock (_gate)
        {
            return Task.FromResult(_tokens.SingleOrDefault(token => token.TokenHash.AsSpan().SequenceEqual(tokenHash)));
        }
    }

    public Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            var stored = _tokens.Single(token => token.Id == current.Id);
            if (stored.IsSpent)
            {
                return Task.FromResult(false);
            }

            SetState(stored, nameof(RefreshToken.ConsumedAt), now);
            _tokens.Add(replacement);
            return Task.FromResult(true);
        }
    }

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var token in _tokens.Where(token => token.FamilyId == familyId && token.RevokedAt is null))
            {
                SetState(token, nameof(RefreshToken.RevokedAt), now);
            }

            _revokedFamilies.Add(familyId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Sets one of the two state properties directly, as a store that was already holding a spent token would have it.</summary>
    public static void SetState(RefreshToken token, string property, DateTimeOffset value) =>
        typeof(RefreshToken).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(token, value);
}
