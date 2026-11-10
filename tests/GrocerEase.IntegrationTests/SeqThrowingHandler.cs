using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;

namespace GrocerEase.IntegrationTests;

/// <summary>A login handler that always crashes, to produce an exception event.</summary>
internal sealed class SeqThrowingHandler : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
{
    public Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("seq handler broke");
}
