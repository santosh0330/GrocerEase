using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;

namespace GrocerEase.Application.Authentication.Handlers;

/// <summary>
/// Exchanges a refresh token for a new pair: a new access token and a new refresh token (rotation). The presented token is
/// consumed, so it works exactly once. If a token that was already used (or revoked) is presented again, somebody holds a copy:
/// the whole family is revoked, which ends that session for the thief and the owner alike. The owner signs in again.
/// </summary>
public sealed class RefreshCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenGenerator refreshTokenGenerator,
    IRefreshTokenPolicy policy,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IJwtTokenGenerator jwtTokenGenerator,
    TimeProvider timeProvider) : ICommandHandler<RefreshCommand, Result<AuthenticationResult>>
{
    public async Task<Result<AuthenticationResult>> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var current = await refreshTokens.GetByHashAsync(refreshTokenGenerator.Hash(command.RefreshToken), cancellationToken);
        if (current is null)
        {
            return Failure();
        }

        if (current.IsSpent)
        {
            // Used before, or revoked: a client that kept the newest token never presents an older one.
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return Failure();
        }

        if (current.IsExpired(now))
        {
            return Failure(); // includes a family that reached its hard end: its last token expires with it
        }

        var user = await userRepository.GetByIdAsync(current.UserId, cancellationToken);
        if (user is null)
        {
            return Failure();
        }

        var newToken = refreshTokenGenerator.Generate();
        var replacement = current.Successor(refreshTokenGenerator.Hash(newToken), now, policy.Lifetime);

        // The check above only looked. This is the step that decides: it consumes the token only if it is still unused, in one
        // indivisible operation. If two requests present the same token at once, one gets true and the other lands here.
        if (!await refreshTokens.TryRotateAsync(current, replacement, now, cancellationToken))
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return Failure();
        }

        // The role is read again on every refresh, so a role changed since the last token shows up in the new one.
        var role = await roleRepository.GetRequiredByIdAsync(user.RoleId, cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateToken(user, role);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, accessToken.Token, accessToken.ExpiresAt, newToken));
    }

    private static Result<AuthenticationResult> Failure() => Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidRefreshToken);
}
