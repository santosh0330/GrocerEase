using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IJwtTokenGenerator jwtTokenGenerator) : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
{
    public async Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // TEMPORARY: plain-text comparison. See User.Password. An unknown email and a wrong password
        // give the same error on purpose, so a caller cannot tell which emails are registered.
        if (user is null || !string.Equals(user.Password, command.Password, StringComparison.Ordinal))
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        var token = jwtTokenGenerator.GenerateToken(user);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, token));
    }
}
