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

public class UnhandledExceptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string InternalDetail = "database password is hunter2";

    private sealed class ThrowingHandler : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
    {
        public Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(InternalDetail);
    }

    [Fact]
    public async Task Post_Login_WhenTheHandlerThrows_Returns500ProblemWithoutInternalDetails()
    {
        using var throwing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, ThrowingHandler>()));
        using var client = throwing.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("ada@example.com", "s3cret!"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("title").GetString());
    }
}
