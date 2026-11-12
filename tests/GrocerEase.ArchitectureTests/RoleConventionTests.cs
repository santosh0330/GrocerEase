using System.Reflection;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// How roles are read. A query that reads the roles table outside the repository bypasses the one place that decides how they are
/// read (and, now that a role carries its claims, the one place that always loads them). How a controller names what it needs is
/// in <see cref="PermissionConventionTests"/>.
/// </summary>
public class RoleConventionTests
{
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

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
