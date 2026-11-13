using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The access token carries the role the user holds, whether it was issued by registering, by logging in or by refreshing. Against
/// the real API and PostgreSQL. A role is changed here by SQL, because nothing in the application changes a role yet.
/// </summary>
public class RoleInTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static string RoleOf(AuthenticationResponse response) => new JsonWebToken(response.Token).GetClaim(JwtClaims.Role).Value;

    private static string[] PermissionsOf(AuthenticationResponse response) =>
        [.. new JsonWebToken(response.Token).Claims
            .Where(claim => string.Equals(claim.Type, JwtClaims.Permission, StringComparison.Ordinal))
            .Select(claim => claim.Value)];

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<AuthenticationResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<AuthenticationResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(refreshToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    [Fact]
    public async Task ANewUser_IsACustomer_AndTheTokenFromRegisteringSaysSo()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var registered = await RegisterAsync(client, email);

        Assert.Equal(RoleNames.Customer, RoleOf(registered));
        Assert.Equal([Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], PermissionsOf(registered), StringComparer.Ordinal);
        Assert.Equal(
            RoleIds.Customer,
            await PostgresServer.ScalarAsync<Guid>(await PostgresServer.AppConnectionStringAsync(), $"select role_id from users where email = '{email}'"));
    }

    [Fact]
    public async Task TheTokenFromLoggingIn_SaysWhatRoleTheUserHoldsNow()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);

        var loggedIn = await LoginAsync(client, email);

        Assert.Equal(RoleNames.Admin, RoleOf(loggedIn));
        Assert.Equal([Permissions.ProductsRead, Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresManage, Permissions.StoresRead], PermissionsOf(loggedIn), StringComparer.Ordinal);
    }

    [Fact]
    public async Task RefreshingPicksUpAChangedRole_ButAnExistingTokenKeepsTheOldOne()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        var registered = await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);

        var refreshed = await RefreshAsync(client, registered.RefreshToken);

        Assert.Equal(RoleNames.Customer, RoleOf(registered)); // a token cannot change after it was signed: this is the staleness to expect
        Assert.Equal([Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], PermissionsOf(registered), StringComparer.Ordinal);
        Assert.Equal(RoleNames.Admin, RoleOf(refreshed));
        Assert.Equal([Permissions.ProductsRead, Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresManage, Permissions.StoresRead], PermissionsOf(refreshed), StringComparer.Ordinal);
    }
}
