using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

internal static class AuthProbe
{
    /// <summary>A new app instance that also serves the probe routes.</summary>
    public static WebApplicationFactory<Program> WithAuthProbe(this ApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly);
            services.AddAuthorizationBuilder().AddPolicy(AuthProbeController.AdminPolicy, policy => policy.RequireClaim("role", "admin"));
        }));

    public static HttpRequestMessage Get(string path, string? authorization = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    public static HttpRequestMessage GetWithToken(string path, string token) =>
        new(HttpMethod.Get, new Uri(path, UriKind.Relative)) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };
}
