using GrocerEase.Api.Controllers;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class AuthControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Result<AuthenticationResult> CannedResult =
        Result.Success(new AuthenticationResult(UserId, "Grace", "Hopper", "grace@example.com", "jwt"));

    private readonly SpyHandler<RegisterCommand> _registerHandler = new(CannedResult);
    private readonly SpyHandler<LoginCommand> _loginHandler = new(CannedResult);

    private AuthController CreateController(
        SpyHandler<RegisterCommand>? registerHandler = null, SpyHandler<LoginCommand>? loginHandler = null) =>
        new(registerHandler ?? _registerHandler, loginHandler ?? _loginHandler)
        {
            ProblemDetailsFactory = new StubProblemDetailsFactory(),
        };

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

    [Fact]
    public async Task Register_WhenTheHandlerFails_ReturnsAProblemForThatError()
    {
        var failing = new SpyHandler<RegisterCommand>(Result.Failure<AuthenticationResult>(
            new ResultError("User.DuplicateEmail", "The email is already registered.", ErrorType.Conflict)));

        var result = await CreateController(registerHandler: failing).Register(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!"), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("User.DuplicateEmail", Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task Login_WhenTheHandlerFails_ReturnsAProblemForThatError()
    {
        var failing = new SpyHandler<LoginCommand>(Result.Failure<AuthenticationResult>(
            new ResultError("Authentication.InvalidCredentials", "The email or password is wrong.", ErrorType.Unauthorized)));

        var result = await CreateController(loginHandler: failing).Login(
            new LoginRequest("ada@example.com", "s3cret!"), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
        Assert.Equal("Authentication.InvalidCredentials", Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    /// <summary>Records the command it receives and returns a canned result.</summary>
    private sealed class SpyHandler<TCommand>(Result<AuthenticationResult> result) : ICommandHandler<TCommand, Result<AuthenticationResult>>
    {
        public TCommand? ReceivedCommand { get; private set; }

        public Task<Result<AuthenticationResult>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ReceivedCommand = command;
            return Task.FromResult(result);
        }
    }
}
