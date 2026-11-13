using System.Security.Claims;
using GrocerEase.Api.Authentication;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.UnitTests.Api;

/// <summary>The authorization policies <c>AddJwtAuthentication</c> registers: one per permission, named after it.</summary>
public class AuthorizationPoliciesTests
{
    private static ServiceProvider Build() =>
        new ServiceCollection().AddLogging().AddJwtAuthentication().BuildServiceProvider();

    private static ClaimsPrincipal Holding(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(permission => new Claim(JwtClaims.Permission, permission)), authenticationType: "Bearer"));

    private static async Task<bool> AllowsAsync(string policyName, ClaimsPrincipal user)
    {
        using var provider = Build();
        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policyName);
        Assert.NotNull(policy);
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy);
        return result.Succeeded;
    }

    [Theory]
    [InlineData(Permissions.ProfileRead)]
    [InlineData(Permissions.RolesRead)]
    [InlineData(Permissions.StoresRead)]
    [InlineData(Permissions.StoresManage)]
    [InlineData(Permissions.ProductsRead)]
    [InlineData(Permissions.ProductsManage)]
    [InlineData(Permissions.InventoryManage)]
    [InlineData(Permissions.OrdersPlace)]
    [InlineData(Permissions.OrdersRead)]
    public async Task EveryPermission_HasAPolicyNamedAfterIt(string permission)
    {
        using var provider = Build();

        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(permission);

        var requirement = Assert.Single(policy!.Requirements.OfType<ClaimsAuthorizationRequirement>());
        Assert.Equal(JwtClaims.Permission, requirement.ClaimType);
        Assert.Equal([permission], requirement.AllowedValues, StringComparer.Ordinal);
    }

    [Fact]
    public void TheTestThatNamesEveryPermission_CoversTheWholeList() =>
        Assert.Equal(
            [Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresRead, Permissions.StoresManage, Permissions.ProductsRead, Permissions.ProductsManage, Permissions.InventoryManage, Permissions.OrdersPlace, Permissions.OrdersRead],
            Permissions.All, StringComparer.Ordinal);

    [Fact]
    public async Task APolicy_AllowsAUserWhoHoldsThePermission_AmongOthers() =>
        Assert.True(await AllowsAsync(Permissions.RolesRead, Holding(Permissions.ProfileRead, Permissions.RolesRead)));

    [Fact]
    public async Task APolicy_RefusesAUserWhoHoldsOnlyAnotherPermission_OrNone()
    {
        Assert.False(await AllowsAsync(Permissions.RolesRead, Holding(Permissions.ProfileRead)));
        Assert.False(await AllowsAsync(Permissions.RolesRead, Holding()));
    }

    [Fact]
    public async Task ARoleNameIsNotAPermission()
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtClaims.Role, RoleNames.Admin)], authenticationType: "Bearer"));

        Assert.False(await AllowsAsync(Permissions.RolesRead, admin));
    }

    [Fact]
    public async Task APermissionIsExact_SoANeighbouringValueIsNotEnough() =>
        Assert.False(await AllowsAsync(Permissions.RolesRead, Holding("roles.read.all", "roles", "ROLES.READ")));

    [Fact]
    public async Task AnUnknownPolicyName_HasNoPolicy()
    {
        using var provider = Build();

        Assert.Null(await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("roles.write"));
    }
}
