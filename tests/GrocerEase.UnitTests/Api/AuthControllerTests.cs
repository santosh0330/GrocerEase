using GrocerEase.Api.Controllers;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class AuthControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly AuthenticationResult CannedResult = new(UserId, "Grace", "Hopper", "grace@example.com", "jwt");

    private readonly SpyHandler<RegisterCommand> _registerHandler = new(CannedResult);
    private readonly SpyHandler<LoginCommand> _loginHandler = new(CannedResult);

    private AuthController CreateController() => new(_registerHandler, _loginHandler);

    [Fact]
    public async Task Register_SendsTheRequestAsACommandToTheHandler()
    {
        await CreateController().Register(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.Equal(new RegisterCommand("Ada", "Lovelace", "ada@example.com", "s3cret!"), _registerHandler.ReceivedCommand);
    }

    [Fact]
    public async Task Register_ReturnsOkWithTheResultMappedToAResponse()
    {
        var result = await CreateController().Register(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!"), CancellationToken.None);

        var response = Assert.IsType<AuthenticationResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(new AuthenticationResponse(UserId, "Grace", "Hopper", "grace@example.com", "jwt"), response);
    }

    [Fact]
    public async Task Login_SendsTheRequestAsACommandToTheHandler()
    {
        await CreateController().Login(new LoginRequest("ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.Equal(new LoginCommand("ada@example.com", "s3cret!"), _loginHandler.ReceivedCommand);
    }

    [Fact]
    public async Task Login_ReturnsOkWithTheResultMappedToAResponse()
    {
        var result = await CreateController().Login(
            new LoginRequest("ada@example.com", "s3cret!"), CancellationToken.None);

        var response = Assert.IsType<AuthenticationResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(new AuthenticationResponse(UserId, "Grace", "Hopper", "grace@example.com", "jwt"), response);
    }

    /// <summary>Records the command it receives and returns a canned result.</summary>
    private sealed class SpyHandler<TCommand>(AuthenticationResult result) : ICommandHandler<TCommand, AuthenticationResult>
    {
        public TCommand? ReceivedCommand { get; private set; }

        public Task<AuthenticationResult> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ReceivedCommand = command;
            return Task.FromResult(result);
        }
    }
}
