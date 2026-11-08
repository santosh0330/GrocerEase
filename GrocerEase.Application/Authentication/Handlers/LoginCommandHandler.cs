using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, AuthenticationResult>
{
    // TEMPORARY: nothing checks the password yet, so any login succeeds with made-up details.
    // Replace with the real use case; do not deploy as is.
    public Task<AuthenticationResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Task.FromResult(new AuthenticationResult(
            Guid.NewGuid(), "FirstName", "LastName", command.Email, "placeholder-token"));
    }
}
