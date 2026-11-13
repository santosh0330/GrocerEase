using System.Reflection;
using System.Text.RegularExpressions;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// The stock ledger only means something if <b>no change to a quantity can skip it</b>: "the sum of a product's movements is its quantity" is
/// true because the statement that changes the quantity is the statement that writes the movement. Two rules keep it so. Nothing outside the
/// inventory repository writes either table (<see cref="StoreScopeConventionTests"/> already says so for every store-owned table). And inside
/// it, every SQL statement that writes <c>inventory_items</c> also writes <c>stock_movements</c>, in that same statement, apart from the one
/// named method that never changes a quantity. The SQL is read from the compiled code, so a statement hidden in a lambda or an async method is
/// found too.
/// </summary>
public class StockLedgerConventionTests
{
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    /// <summary>
    /// The methods allowed to write a stock row without a movement. <c>SetLowStockThresholdAsync</c> writes the level, and makes the row at
    /// zero when there is none: a quantity of zero is explained by no movements, so there is nothing to write.
    /// </summary>
    private static readonly string[] MethodsThatNeverChangeAQuantity = [nameof(InventoryItemRepository.SetLowStockThresholdAsync)];

    private static readonly Regex WritesStock = new(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+inventory_items\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
    private static readonly Regex WritesLedger = new(@"\bINSERT\s+INTO\s+stock_movements\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    // The method a string literal belongs to: the method itself, or, for an async method or a lambda, the compiler-generated type named after it
    // ("<TryAdjustAsync>d__3", "<>c__DisplayClass1_0" inside "<SetLowStock>b__0").
    private static string MethodOf(TypeDefinition type, MethodDefinition method)
    {
        var generated = Regex.Match(type.Name, "^<(?<name>[^>]+)>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (generated.Success && generated.Groups["name"].Value.Length > 0)
        {
            return generated.Groups["name"].Value;
        }

        return method.Name;
    }

    private static List<(string Method, string Sql)> StatementsOf(string topLevelTypeName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => string.Equals(TopLevel(type).Name, topLevelTypeName, StringComparison.Ordinal))
            .SelectMany(type => type.Methods.Where(method => method.HasBody).Select(method => (type, method)))
            .SelectMany(pair => pair.method.Body.Instructions
                .Where(instruction => instruction.OpCode.Code is Code.Ldstr && instruction.Operand is string)
                .Select(instruction => (MethodOf(pair.type, pair.method), (string)instruction.Operand)))];
    }

    /// <summary>The statements that write a stock row but not, in the same statement, the ledger, and are not by an exempt method.</summary>
    private static List<string> StatementsThatSkipTheLedger(IEnumerable<(string Method, string Sql)> statements) =>
        [.. statements
            .Where(statement => WritesStock.IsMatch(statement.Sql) && !WritesLedger.IsMatch(statement.Sql))
            .Where(statement => !MethodsThatNeverChangeAQuantity.Contains(statement.Method, StringComparer.Ordinal))
            .Select(statement => $"{statement.Method}: {Shorten(statement.Sql)}")];

    // One line, at most 90 characters: enough to recognise a statement in a failure message.
    private static string Shorten(string sql)
    {
        var oneLine = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 90 ? oneLine : oneLine[..90];
    }

    [Fact]
    public void EverySqlStatementThatWritesAStockRow_AlsoWritesTheLedger_InTheSameStatement()
    {
        var skips = StatementsThatSkipTheLedger(StatementsOf(nameof(InventoryItemRepository)));

        Assert.True(
            skips.Count == 0,
            $"A quantity may only change in the statement that writes its movement, or the ledger stops adding up: {string.Join(" | ", skips)}");
    }

    [Fact]
    public void TheInventoryRepository_WritesOnlyThroughRawSql_SoTheRuleAboveSeesEveryWrite()
    {
        // An ExecuteUpdate, an Add or a SaveChanges would change a stock row in a way the SQL scan above cannot see.
        var other = new[] { "SaveChanges", "SaveChangesAsync", "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync", "Add", "AddAsync", "AddRange", "Remove", "Update", "Attach" };
        var calls = EfCallsOf(nameof(InventoryItemRepository)).Where(name => other.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(calls.Count == 0, $"InventoryItemRepository must write stock only with SQL that writes the ledger too, not with: {string.Join(", ", calls)}");
    }

    private static List<string> EfCallsOf(string topLevelTypeName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => string.Equals(TopLevel(type).Name, topLevelTypeName, StringComparison.Ordinal))
            .SelectMany(type => type.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions))
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt && instruction.Operand is MethodReference)
            .Select(instruction => (MethodReference)instruction.Operand)
            .Where(called => called.DeclaringType.FullName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(called => called.Name)];
    }

    [Fact]
    public void AStockChange_CannotBeMadeWithoutSayingWhyAndWho()
    {
        // The only way to change a quantity takes a StockChange (an amount, a reason, an actor, an order), never a bare number.
        var adjusters = typeof(IInventoryItemRepository).GetMethods().Where(method => method.Name.Contains("Adjust", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(adjusters);
        Assert.All(adjusters, method => Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(StockChange)));
    }

    [Fact]
    public void TheRule_CanFail_BecauseItSeesAStatementThatSkipsTheLedger_AndSpareOnesThatDoNot()
    {
        (string, string)[] statements =
        [
            ("Sneaky", "UPDATE inventory_items SET quantity_on_hand = quantity_on_hand + 1 WHERE product_id = {0}"),
            ("SneakyInsert", "insert into INVENTORY_ITEMS (product_id, store_id, quantity_on_hand) values ({0}, {1}, 5)"),
            ("Honest", "WITH changed AS (UPDATE inventory_items SET quantity_on_hand = quantity_on_hand + 1 RETURNING quantity_on_hand), moved AS (INSERT INTO stock_movements (id) SELECT 1 FROM changed) SELECT 1"),
            ("SetLowStockThresholdAsync", "INSERT INTO inventory_items (product_id, store_id, quantity_on_hand, low_stock_threshold) VALUES ({0}, {1}, 0, 3)"),
            ("Reader", "SELECT quantity_on_hand FROM inventory_items"),
        ];

        Assert.Equal(["Sneaky", "SneakyInsert"], StatementsThatSkipTheLedger(statements).Select(text => text.Split(':')[0]), StringComparer.Ordinal);
    }

    [Fact]
    public void TheScan_FindsTheRealStatements_SoAnEmptyAnswerMeansSomething()
    {
        var statements = StatementsOf(nameof(InventoryItemRepository));
        var writers = statements.Where(statement => WritesStock.IsMatch(statement.Sql)).ToList();

        // The addition and the removal write a stock row and a movement; the level statement writes a stock row and no movement.
        Assert.Equal(2, writers.Count(statement => string.Equals(statement.Method, nameof(InventoryItemRepository.TryAdjustAsync), StringComparison.Ordinal) && WritesLedger.IsMatch(statement.Sql)));
        Assert.Equal(1, writers.Count(statement => string.Equals(statement.Method, nameof(InventoryItemRepository.SetLowStockThresholdAsync), StringComparison.Ordinal) && !WritesLedger.IsMatch(statement.Sql)));
        Assert.Equal(3, writers.Count);
    }
}
