using System.Reflection;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using NetArchTest.Rules;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// How a controller says who may call it. A policy name that matches no registered policy (a typo, or a permission that was
/// renamed) makes ASP.NET Core throw: every request to that route becomes a <c>500</c>, even one with no token (tried it). The
/// failure is loud, but it is found by someone calling the route, so it is better found here. And a role name in a guard brings back
/// what permissions replaced: the endpoint would change whenever the roles are reshuffled.
/// </summary>
public class PermissionConventionTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IEnumerable<(string Where, AuthorizeAttribute Attribute)> AuthorizeAttributesInControllers() =>
        Types.InAssembly(Api).That().ResideInNamespace("GrocerEase.Api.Controllers").GetTypes().SelectMany(controller =>
            controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(attribute => (controller.Name, attribute))
                .Concat(controller
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                        .Select(attribute => ($"{controller.Name}.{method.Name}", attribute)))));

    /// <summary>The policy names an attribute asks for that are not permissions the application knows.</summary>
    private static string[] UnknownPolicies(AuthorizeAttribute attribute) =>
        attribute.Policy is { } policy && !Permissions.All.Contains(policy, StringComparer.Ordinal) ? [policy] : [];

    [Fact]
    public void EveryPolicyAGuardNames_IsAPermissionTheApplicationKnows()
    {
        var offenders = AuthorizeAttributesInControllers()
            .SelectMany(guard => UnknownPolicies(guard.Attribute).Select(policy => $"{guard.Where}: '{policy}'"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"[Authorize(Policy = ...)] must name a permission from Permissions.All, or every request to that route fails with a 500: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoControllerGuardsByRoleName_BecauseThePermissionIsWhatTheEndpointNeeds()
    {
        var offenders = AuthorizeAttributesInControllers()
            .Where(guard => !string.IsNullOrWhiteSpace(guard.Attribute.Roles))
            .Select(guard => $"{guard.Where}: Roles = '{guard.Attribute.Roles}'")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Guard with [Authorize(Policy = Permissions.X)], not a role name, so reshuffling the roles changes no endpoint: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheGuardRules_AreNotEmpty_AndCanFail()
    {
        // Guards the two tests above against passing by accident: at least one controller names a permission, and the helpers
        // do catch a misspelt policy and a role name.
        Assert.Contains(AuthorizeAttributesInControllers(), guard => guard.Attribute.Policy is not null);
        Assert.Equal(["roles.raed"], UnknownPolicies(new AuthorizeAttribute { Policy = "roles.raed" }));
        Assert.Empty(UnknownPolicies(new AuthorizeAttribute { Policy = Permissions.RolesRead }));
        Assert.Empty(UnknownPolicies(new AuthorizeAttribute())); // a plain [Authorize] names no policy: any signed-in user
    }
}
