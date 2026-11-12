using System.Reflection;
using GrocerEase.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// Who begins a database transaction. EF Core does not nest them, so a repository that begins its own cannot run inside the unit
/// of work that wraps a transactional handler (<c>EfUnitOfWorkTests</c> shows the failure). Today exactly one repository does, the
/// refresh-token one, and its handlers are not transactional. Anyone adding a second one is meant to stop at this test and decide:
/// join the unit of work instead (the repository then needs no transaction of its own), or keep the handlers that use it out of one.
/// </summary>
public class TransactionConventionTests
{
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    // Reading the IL, not the source, so a call hidden in a lambda or an async method (the compiler moves those into a nested type) is
    // found too, and is credited to the type that wrote it.
    private static List<string> TopLevelTypesThatBeginTransactions()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code is Code.Call or Code.Callvirt
                && instruction.Operand is MethodReference called
                && called.Name is "BeginTransaction" or "BeginTransactionAsync")))
            .Select(type => TopLevel(type).Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void OnlyTheUnitOfWork_AndTheRefreshTokenRepository_BeginTransactions() =>
        Assert.Equal([nameof(EfUnitOfWork), nameof(RefreshTokenRepository)], TopLevelTypesThatBeginTransactions(), StringComparer.Ordinal);

    [Fact]
    public void TheTransactionRule_CanFail_BecauseBothTypesDoBeginOne()
    {
        // Guards the test above against passing by accident (a wrong method name would match nothing, and an empty list is not a
        // list of the two expected types, but a test that cannot see the types it names should say so).
        var types = TopLevelTypesThatBeginTransactions();

        Assert.Contains(types, name => string.Equals(name, nameof(EfUnitOfWork), StringComparison.Ordinal));
        Assert.Contains(types, name => string.Equals(name, nameof(RefreshTokenRepository), StringComparison.Ordinal));
    }
}
