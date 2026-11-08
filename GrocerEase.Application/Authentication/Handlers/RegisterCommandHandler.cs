using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;

namespace GrocerEase.Application.Authentication.Handlers;

public sealed class RegisterCommandHandler : ICommandHandler<RegisterCommand, AuthenticationResult>
{
    // TEMPORARY: no user store, password hasher or token generator exists yet, so this
    // returns a made-up id and token. Replace with the real use case; do not deploy as is.
    public Task<AuthenticationResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Task.FromResult(new AuthenticationResult(
            Guid.NewGuid(), command.FirstName, command.LastName, command.Email, "placeholder-token"));
    }
}
