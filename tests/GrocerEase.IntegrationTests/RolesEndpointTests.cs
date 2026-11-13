using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Roles;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using static GrocerEase.IntegrationTests.AuthProbe;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// GET /roles: guarded by the roles.read permission. No token is a 401, a signed-in customer is a 403, an admin gets the list. Against the
/// real API and PostgreSQL. Nothing in the application can make an admin yet, so a test promotes a user with SQL.
/// </summary>
public class RolesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

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
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/roles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.Required", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ACustomer_Is403_WithTheForbiddenProblem()
    {
        using var client = factory.CreateClient();
        var registered = await RegisterAsync(client, UniqueEmail());

        using var request = GetWithToken("/roles", registered.Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnAdmin_Gets200_WithEveryRole()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);
        var admin = await LoginAsync(client, email); // a new token, because a role is read when a token is made

        using var request = GetWithToken("/roles", admin.Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.NotNull(roles);
        Assert.Equal(
            [(RoleIds.Admin, RoleNames.Admin), (RoleIds.Customer, RoleNames.Customer), (RoleIds.StoreUser, RoleNames.StoreUser)],
            roles.Select(role => (role.Id, role.Name)));
        Assert.Equal([Permissions.OrdersReadAll, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresManage, Permissions.StoresRead], roles[0].Permissions, StringComparer.Ordinal);
        Assert.Equal([Permissions.OrdersCancel, Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], roles[1].Permissions, StringComparer.Ordinal);
        Assert.Equal([Permissions.InventoryManage, Permissions.OrdersManage, Permissions.ProductsManage, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], roles[2].Permissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AStoreUser_HasTheirOwnRoleAndPermissionsInTheToken_AndIs403OnRoles()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await TestUsers.MakeStoreUserAsync(email);
        var storeUser = await LoginAsync(client, email);

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(storeUser.Token);
        using var request = GetWithToken("/roles", storeUser.Token);
        var response = await client.SendAsync(request);

        Assert.Equal(RoleNames.StoreUser, token.GetClaim(JwtClaims.Role).Value);
        Assert.Equal(
            [Permissions.InventoryManage, Permissions.OrdersManage, Permissions.ProductsManage, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead],
            token.Claims.Where(claim => string.Equals(claim.Type, JwtClaims.Permission, StringComparison.Ordinal)).Select(claim => claim.Value),
            StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminsOldToken_IsStillACustomersToken_UntilTheyGetANewOne()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        var registered = await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);

        using var request = GetWithToken("/roles", registered.Token); // issued while they were a customer
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ATokenThatClaimsThePermissionButIsSignedWithAnotherKey_Is401()
    {
        using var client = factory.CreateClient();
        var forged = TestTokens.Create(
            key: "another-signing-key-0123456789-abcdefghij",
            extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { [JwtClaims.Permission] = new[] { Permissions.RolesRead } });

        using var request = GetWithToken("/roles", forged);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASignedTokenWithThePermission_Is200_WithoutTouchingTheDatabaseRow()
    {
        using var client = factory.CreateClient();
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtClaims.Permission] = new[] { Permissions.ProfileRead, Permissions.RolesRead }, // an array: one claim per entry
        });

        using var request = GetWithToken("/roles", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ATokenThatSaysAdminButCarriesNoPermission_Is403_BecauseThePermissionDecides_NotTheName()
    {
        using var client = factory.CreateClient();
        // What a token made before permissions existed looks like: a role, and nothing the role may do.
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { [JwtClaims.Role] = RoleNames.Admin });

        using var request = GetWithToken("/roles", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATokenWithOnlyTheProfilePermission_Is403_AndOneWithOnlyTheRolesPermissionIs200()
    {
        using var client = factory.CreateClient();
        Dictionary<string, object> Permission(string permission) =>
            new(StringComparer.Ordinal) { [JwtClaims.Permission] = new[] { permission } };

        using var profileOnly = GetWithToken("/roles", TestTokens.Create(extraClaims: Permission(Permissions.ProfileRead)));
        using var rolesOnly = GetWithToken("/roles", TestTokens.Create(extraClaims: Permission(Permissions.RolesRead)));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(profileOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(rolesOnly)).StatusCode);
    }

    [Fact]
    public async Task ATokenWithNoRoleClaim_Is403()
    {
        using var client = factory.CreateClient();

        using var request = GetWithToken("/roles", TestTokens.Create());
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
