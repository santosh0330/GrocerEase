using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class RegisterCommandHandler(
    IUserRepository userRepository,
    IJwtTokenGenerator jwtTokenGenerator) : ICommandHandler<RegisterCommand, Result<AuthenticationResult>>
{
    public async Task<Result<AuthenticationResult>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await userRepository.GetByEmailAsync(command.Email, cancellationToken) is not null)
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.DuplicateEmail);
        }

        // TEMPORARY: the password is stored as given. See User.Password.
        var user = new User(Guid.NewGuid(), command.FirstName, command.LastName, command.Email, command.Password);
        await userRepository.AddAsync(user, cancellationToken);

        var token = jwtTokenGenerator.GenerateToken(user);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, token));
    }
}
