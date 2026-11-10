using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Simultaneous refreshes through the real API and the real database. A refresh token may be used exactly once, and the "exactly"
/// is what a race would break: two requests could both read the token as unused and both be given a new one.
/// </summary>
public class RefreshConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", UniqueEmail(), Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken, ManualResetEventSlim gate)
    {
        await Task.Yield();
        gate.Wait(); // every request is ready, then they all go at the same moment
        return await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(refreshToken));
    }

    private static async Task<List<HttpResponseMessage>> RefreshAllAtOnceAsync(HttpClient client, IEnumerable<string> tokens)
    {
        using var gate = new ManualResetEventSlim(false);
        var requests = tokens.Select(token => Task.Run(() => RefreshAsync(client, token, gate))).ToList();
        await Task.Delay(300);
        gate.Set();
        return [.. await Task.WhenAll(requests)];
    }

    [Fact]
    public async Task WhenManyRequestsPresentTheSameToken_ExactlyOneIsAccepted_AndTheRestAreRefusedNotFailed()
    {
        const int rounds = 5;
        const int requestsPerRound = 12;
        using var client = factory.CreateClient();

        for (var round = 0; round < rounds; round++)
        {
            var signedIn = await RegisterAsync(client);

            var responses = await RefreshAllAtOnceAsync(client, Enumerable.Repeat(signedIn.RefreshToken, requestsPerRound));

            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            // The rest are plain refusals: never a 500 from a lost race, a deadlock or a duplicate key.
            Assert.Equal(requestsPerRound - 1, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
            foreach (var refused in responses.Where(response => response.StatusCode == HttpStatusCode.Unauthorized))
            {
                var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();
                Assert.Equal("Authentication.InvalidRefreshToken", problem!.Extensions["code"]?.ToString());
            }
        }
    }

    [Fact]
    public async Task AfterARace_NoTokenOfThatSessionWorks_NotEvenTheWinnersNewOne()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);

        var responses = await RefreshAllAtOnceAsync(client, Enumerable.Repeat(signedIn.RefreshToken, 8));
        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var winnersToken = (await winner.Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken;

        // A losing request saw a token that was already used, which is what a thief replaying it looks like: the session is revoked.
        var later = await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(winnersToken));

        Assert.Equal(HttpStatusCode.Unauthorized, later.StatusCode);
    }

    [Fact]
    public async Task RefreshesOfDifferentSessionsAtOnce_AreAllAccepted_BecauseTheyDoNotShareAnything()
    {
        using var client = factory.CreateClient();
        var sessions = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            sessions.Add((await RegisterAsync(client)).RefreshToken);
        }

        var responses = await RefreshAllAtOnceAsync(client, sessions);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    [Fact]
    public async Task ARace_HandsOutExactlyOneNewToken_AndItIsNotTheOldOne()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);

        var responses = await RefreshAllAtOnceAsync(client, Enumerable.Repeat(signedIn.RefreshToken, 10));

        var handedOut = new List<string>();
        foreach (var ok in responses.Where(response => response.StatusCode == HttpStatusCode.OK))
        {
            handedOut.Add((await ok.Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken);
        }

        Assert.Single(handedOut);
        Assert.NotEqual(signedIn.RefreshToken, handedOut[0], StringComparer.Ordinal);
    }
}
