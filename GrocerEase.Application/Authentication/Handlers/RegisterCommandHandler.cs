using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class RegisterCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator) : ICommandHandler<RegisterCommand, Result<AuthenticationResult>>
{
    public async Task<Result<AuthenticationResult>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Checked before hashing: hashing is deliberately slow, so do not spend it on a request that will be refused.
        // The check is only a shortcut; TryAddAsync below is what actually guarantees there is one user per email.
        if (await userRepository.GetByEmailAsync(command.Email, cancellationToken) is not null)
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.DuplicateEmail);
        }

        var user = new User(
            Guid.CreateVersion7(), command.FirstName, command.LastName, command.Email, passwordHasher.Hash(command.Password));
        if (!await userRepository.TryAddAsync(user, cancellationToken))
        {
            // Another registration took the email between the check above and this add.
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.DuplicateEmail);
        }

        var token = jwtTokenGenerator.GenerateToken(user);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, token));
    }
}
