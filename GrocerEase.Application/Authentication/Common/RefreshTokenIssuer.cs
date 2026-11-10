using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>
/// Starts a session: makes a refresh token, stores only its hash, and returns the token itself for the client. Registering and
/// logging in both end this way, so the steps live here once, in the one place that holds the plain token for the one moment
/// it exists on the server.
/// </summary>
public sealed class RefreshTokenIssuer(
    IRefreshTokenRepository repository,
    IRefreshTokenGenerator generator,
    IRefreshTokenPolicy policy,
    TimeProvider timeProvider)
{
    /// <summary>Begins a new family of refresh tokens for the user and returns the plain token. The server keeps no copy of it.</summary>
    public async Task<string> StartSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var token = generator.Generate();
        var stored = RefreshToken.Start(userId, generator.Hash(token), timeProvider.GetUtcNow(), policy.Lifetime, policy.MaxLifetime);

        await repository.AddAsync(stored, cancellationToken);
        return token;
    }
}
