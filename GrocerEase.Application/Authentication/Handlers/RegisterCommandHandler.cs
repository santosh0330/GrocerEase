using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class RegisterCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    RefreshTokenIssuer refreshTokenIssuer) : ICommandHandler<RegisterCommand, Result<AuthenticationResult>>
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

        // A new user is always a customer. The role is seeded by a migration, so a missing one is a broken database, not something
        // the caller did wrong: it is an exception (a generic 500 that is logged), not a Result error.
        var customer = await roleRepository.GetRequiredByNameAsync(RoleNames.Customer, cancellationToken);

        var user = new User(
            Guid.CreateVersion7(), command.FirstName, command.LastName, command.Email, passwordHasher.Hash(command.Password), customer.Id);
        if (!await userRepository.TryAddAsync(user, cancellationToken))
        {
            // Another registration took the email between the check above and this add.
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.DuplicateEmail);
        }

        var token = jwtTokenGenerator.GenerateToken(user, customer);
        var refreshToken = await refreshTokenIssuer.StartSessionAsync(user.Id, cancellationToken);
        return Result.Success(new AuthenticationResult(user.Id, user.FirstName, user.LastName, user.Email, token, refreshToken));
    }
}
