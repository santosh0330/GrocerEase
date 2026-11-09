using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The real handlers' failures are covered in <see cref="AuthEndpointsTests"/>. These tests swap in a handler that returns
/// a chosen failure and check the whole pipeline turns it into a problem response.
/// </summary>
public class AuthFailureEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class FailingHandler<TCommand>(ResultError error) : ICommandHandler<TCommand, Result<AuthenticationResult>>
    {
        public Task<Result<AuthenticationResult>> Handle(TCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<AuthenticationResult>(error));
    }

    [Fact]
    public async Task Post_Register_WhenTheHandlerReturnsAConflict_Returns409Problem()
    {
        var error = new ResultError("User.DuplicateEmail", "The email is already registered.", ErrorType.Conflict);
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<RegisterCommand, Result<AuthenticationResult>>>(_ => new FailingHandler<RegisterCommand>(error))));
        using var client = failing.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Sup3r-s3cret!"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "User.DuplicateEmail", "The email is already registered.");
    }

    [Fact]
    public async Task Post_Login_WhenTheHandlerReturnsUnauthorized_Returns401Problem()
    {
        var error = new ResultError("Authentication.InvalidCredentials", "The email or password is wrong.", ErrorType.Unauthorized);
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>(_ => new FailingHandler<LoginCommand>(error))));
        using var client = failing.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("ada@example.com", "s3cret!"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Authentication.InvalidCredentials", "The email or password is wrong.");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.Equal(detail, root.GetProperty("detail").GetString());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.True(root.TryGetProperty("traceId", out _), "A problem built by the controller carries a traceId.");
    }
}
