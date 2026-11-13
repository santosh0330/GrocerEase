using System.Reflection;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// A product's <c>Version</c> only means something if <b>every change to the row changes it</b>: a client that read version 3 is told the
/// product moved on only if every writer moved the version. So the product repository may not change a product in a way that leaves the
/// version out. Read from the compiled code, so a statement inside an async method or a lambda is found too.
/// </summary>
/// <remarks>
/// <para>
/// What this rule can see: every method that changes products with <c>ExecuteUpdate</c> must <b>mention</b> <c>Product.Version</c>, and no
/// method may change a product in a way that skips the statements (tracked updates, <c>Update</c>, <c>Attach</c>) or delete one (a product
/// is archived, never deleted). What it cannot see is whether the mention is the bump (<c>Version + 1</c>) or only the check (<c>WHERE
/// version = ...</c>): the integration tests say that an edit is the next version, and this rule keeps a new writer from forgetting the version
/// altogether.
/// </para>
/// </remarks>
public class ProductVersionConventionTests
{
    private static readonly string[] Updating = ["ExecuteUpdate", "ExecuteUpdateAsync"];

    // Writing around the statements the rule reads, or removing what is only ever archived.
    private static readonly string[] Forbidden = ["Update", "UpdateRange", "Attach", "AttachRange", "Remove", "RemoveRange", "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteSql", "ExecuteSqlAsync", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQuery", "SqlQueryRaw"];

    // The method a piece of compiled code belongs to. The compiler moves an async method's body into "<TryUpdateAsync>d__2" and a lambda that
    // captures something into a closure class, whose own name ("<>c__DisplayClass2_0") says nothing but whose method ("<TryUpdateAsync>b__0") does.
    // So the owner is the name between the angle brackets of the type, or, when there is none, of the method.
    private static string MethodOf(TypeDefinition type, MethodDefinition method) =>
        InsideBrackets(type.Name) ?? InsideBrackets(method.Name) ?? method.Name;

    private static string? InsideBrackets(string name)
    {
        var end = name.IndexOf('>', StringComparison.Ordinal);
        return name.StartsWith('<') && end > 1 ? name[1..end] : null;
    }

    private sealed record Use(string Method, string EfCall, bool MentionsVersion);

    private static List<Use> UsesIn(string assemblyPath, string topLevelTypeName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        // The class asked for, or anything the compiler nested inside it (a state machine, a lambda holder): a class may be nested in another.
        static bool IsInside(TypeDefinition type, string name) =>
            string.Equals(type.Name, name, StringComparison.Ordinal) || (type.DeclaringType is not null && IsInside(type.DeclaringType, name));

        var versionOfProduct = typeof(Product).FullName;
        var instructions = assembly.MainModule.GetTypes()
            .Where(type => IsInside(type, topLevelTypeName))
            .SelectMany(type => type.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions.Select(instruction => (Method: MethodOf(type, method), instruction))))
            .ToList();

        return [.. instructions
            .Where(item => item.instruction.OpCode.Code is Code.Call or Code.Callvirt && item.instruction.Operand is MethodReference called
                && called.DeclaringType.FullName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                && (Updating.Contains(called.Name, StringComparer.Ordinal) || Forbidden.Contains(called.Name, StringComparer.Ordinal)))
            .Select(item => new Use(
                item.Method,
                ((MethodReference)item.instruction.Operand).Name,
                instructions.Any(other => string.Equals(other.Method, item.Method, StringComparison.Ordinal)
                    && other.instruction.Operand is MethodReference mentioned
                    && string.Equals(mentioned.Name, "get_Version", StringComparison.Ordinal)
                    && string.Equals(mentioned.DeclaringType.FullName, versionOfProduct, StringComparison.Ordinal))))];
    }

    private static List<string> Violations(IEnumerable<Use> uses) =>
        [.. uses
            .Where(use => Forbidden.Contains(use.EfCall, StringComparer.Ordinal) || !use.MentionsVersion)
            .Select(use => Forbidden.Contains(use.EfCall, StringComparer.Ordinal)
                ? $"{use.Method} calls {use.EfCall}, which writes around the version (or removes a product)"
                : $"{use.Method} changes products without mentioning their Version")
            .Distinct(StringComparer.Ordinal)];

    [Fact]
    public void EveryMethodThatChangesProducts_MentionsTheirVersion_AndNoneWritesAroundIt()
    {
        var violations = Violations(UsesIn(typeof(ProductRepository).Assembly.Location, nameof(ProductRepository)));

        Assert.True(violations.Count == 0, $"A product's version must change with every change to it: {string.Join("; ", violations)}");
    }

    [Fact]
    public void TheScan_FindsTheRealWriters_SoAnEmptyAnswerMeansSomething()
    {
        var writers = UsesIn(typeof(ProductRepository).Assembly.Location, nameof(ProductRepository))
            .Where(use => Updating.Contains(use.EfCall, StringComparer.Ordinal)).Select(use => use.Method).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        // The edit and the archiving: the two ways a product row changes after it is made.
        Assert.Equal([nameof(ProductRepository.TryArchiveAsync), nameof(ProductRepository.TryUpdateAsync)], writers, StringComparer.Ordinal);
    }

    // Canaries: two writers in this assembly, one that forgets the version and one that does not, and one that deletes.
    private sealed class Sneaky(AppDbContext db)
    {
        public Task<int> RenameAsync(Guid id) => db.Products.Where(product => product.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Name, "x"));

        public Task<int> DeleteAsync(Guid id) => db.Products.Where(product => product.Id == id).ExecuteDeleteAsync();
    }

    private sealed class Honest(AppDbContext db)
    {
        public Task<int> RenameAsync(Guid id) =>
            db.Products.Where(product => product.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Name, "x").SetProperty(product => product.Version, product => product.Version + 1));
    }

    [Fact]
    public void TheRule_CanFail_BecauseItCatchesAWriterThatForgetsTheVersion_AndADelete_AndSparesOneThatDoesNot()
    {
        var here = Assembly.GetExecutingAssembly().Location;

        var sneaky = Violations(UsesIn(here, nameof(Sneaky)));
        var honest = Violations(UsesIn(here, nameof(Honest)));

        Assert.Equal(2, sneaky.Count);
        Assert.Contains(sneaky, violation => violation.Contains("RenameAsync changes products without mentioning their Version", StringComparison.Ordinal));
        Assert.Contains(sneaky, violation => violation.Contains("ExecuteDeleteAsync", StringComparison.Ordinal));
        Assert.Empty(honest);
    }
}
