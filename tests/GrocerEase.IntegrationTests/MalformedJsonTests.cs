using System.Net;
using System.Text;
using System.Text.Json;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A body that is not valid JSON is rejected by MVC model binding (the <c>[ApiController]</c> filter) before the action runs,
/// so it never reaches the handlers or the exception handlers. These tests pin that: a 400 problem, not a 500.
/// </summary>
public class MalformedJsonTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TruncatedBody = """{ "email": """;

    private sealed class SpyHandler<TCommand> : ICommandHandler<TCommand, Result<AuthenticationResult>>
    {
        public int Calls { get; private set; }

        public Task<Result<AuthenticationResult>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The handler must not run for a malformed body.");
        }
    }

    [Fact]
    public async Task Post_Register_WithMalformedJson_Returns400ProblemWithoutCallingTheHandler()
    {
        var spy = new SpyHandler<RegisterCommand>();
        using var spied = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<RegisterCommand, Result<AuthenticationResult>>>(_ => spy)));

        await AssertMalformedBodyRejectedAsync(spied, "/auth/register");

        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public async Task Post_Login_WithMalformedJson_Returns400ProblemWithoutCallingTheHandler()
    {
        var spy = new SpyHandler<LoginCommand>();
        using var spied = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>(_ => spy)));

        await AssertMalformedBodyRejectedAsync(spied, "/auth/login");

        Assert.Equal(0, spy.Calls);
    }

    private static async Task AssertMalformedBodyRejectedAsync(WebApplicationFactory<Program> host, string path)
    {
        using var client = host.CreateClient();
        using var content = new StringContent(TruncatedBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("$.email", out _), "The parser error is keyed by the JSON path where parsing stopped.");
    }
}
