using System.Reflection;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// How roles are used. A guard that names a role nobody holds (a typo, or a role that was renamed) locks every caller out without
/// a single error, and a query that reads the roles table outside the repository bypasses the one place that decides how they are read.
/// </summary>
public class RoleConventionTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    /// <summary>The roles an <c>[Authorize(Roles = "A,B")]</c> value names that the application does not know.</summary>
    private static string[] UnknownRoles(string? roles) =>
        [.. (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(role => !RoleNames.All.Contains(role, StringComparer.Ordinal))];

    private static IEnumerable<(string Where, AuthorizeAttribute Attribute)> AuthorizeAttributesInControllers() =>
        Types.InAssembly(Api).That().ResideInNamespace("GrocerEase.Api.Controllers").GetTypes().SelectMany(controller =>
            controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(attribute => (controller.Name, attribute))
                .Concat(controller
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                        .Select(attribute => ($"{controller.Name}.{method.Name}", attribute)))));

    [Fact]
    public void EveryRoleAGuardNames_IsARoleTheApplicationKnows()
    {
        var offenders = AuthorizeAttributesInControllers()
            .Where(guard => guard.Attribute.Roles is not null)
            .SelectMany(guard => UnknownRoles(guard.Attribute.Roles).Select(role => $"{guard.Where}: '{role}'"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"[Authorize(Roles = ...)] must name roles from RoleNames.All, or no caller is ever let in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheGuardRule_IsNotEmpty_AndCanFail()
    {
        // Guards the test above against passing by accident: a misspelt role is caught. (No controller names a role any more: they
        // name permissions, so the test above has nothing to check until one does.)
        Assert.NotEmpty(AuthorizeAttributesInControllers());
        Assert.Equal(["Admn"], UnknownRoles("Admn, Admin"));
        Assert.Empty(UnknownRoles("Admin,Customer"));
    }

    // A role is read from the database through AppDbContext.Roles (or Set<Role>()). Reading the IL, not the source, so a call hidden
    // in a lambda or an async method is found too.
    private static List<string> TopLevelTypesThatQueryRoles()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);
        var contextType = typeof(AppDbContext).FullName;
        var roleType = typeof(Role).FullName;

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code is Code.Call or Code.Callvirt
                && instruction.Operand is MethodReference called
                && ((string.Equals(called.Name, "get_Roles", StringComparison.Ordinal)
                        && string.Equals(called.DeclaringType.FullName, contextType, StringComparison.Ordinal))
                    || (string.Equals(called.Name, "Set", StringComparison.Ordinal)
                        && called is GenericInstanceMethod { GenericArguments: [var entity] }
                        && string.Equals(entity.FullName, roleType, StringComparison.Ordinal))))))
            .Select(type => TopLevel(type).Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void RolesAreQueriedOnlyByTheRoleRepository_AndTheContextThatExposesThem() =>
        Assert.Equal([nameof(AppDbContext), nameof(RoleRepository)], TopLevelTypesThatQueryRoles(), StringComparer.Ordinal);

    [Fact]
    public void TheRoleQueryRule_CanFail_BecauseTheRoleRepositoryDoesQueryRoles() =>
        Assert.Contains(TopLevelTypesThatQueryRoles(), name => string.Equals(name, nameof(RoleRepository), StringComparison.Ordinal));
}
