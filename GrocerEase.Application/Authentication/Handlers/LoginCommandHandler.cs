using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    RefreshTokenIssuer refreshTokenIssuer) : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
{
    public async Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // Verify runs even when there is no such user (it is given null), so an unknown email costs the same time as a
        // wrong password. Then both give the same error, so a caller cannot tell which emails are registered.
        var passwordMatches = passwordHasher.Verify(user?.PasswordHash, command.Password);
        if (user is null || !passwordMatches)
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        var token = jwtTokenGenerator.GenerateToken(user);
        var refreshToken = await refreshTokenIssuer.StartSessionAsync(user.Id, cancellationToken);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, token, refreshToken));
    }
}
