using System.Reflection;
using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// Data that belongs to a store must never be asked for without saying which store. A permission (<c>products.manage</c>) says what
/// kind of user may do something; only a membership check says "in this store", and a query that forgets the store would hand one
/// store's data to another's user. The rules are the usual command/query split. <b>Writing</b> goes through the repository of the table:
/// every repository method that deals with a store-owned entity takes the <c>storeId</c> (or an entity that carries it), so an unscoped
/// query cannot be written through the port, and nothing else reads or changes the table, so it cannot be written around the port either.
/// <b>Listings that need several tables at once</b> are read models (<see cref="ReadModels"/>): named here one by one, they may join
/// store-owned tables, but must take the <c>storeId</c> on every method of their port and must never write. "Store-owned" is any domain
/// entity with a <c>StoreId</c>, so the next one (an order, an inventory item) is covered the day it is added.
/// </summary>
public class StoreScopeConventionTests
{
    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    /// <summary>
    /// The read models: queries that answer one listing needing two store-owned tables at once ("the live products whose stock is at or below
    /// its level"), which neither table's own repository can page (a page cut before the archived products are removed comes back short while
    /// the total still counts them). A repository is for loading and saving what the domain changes; a listing is not that, so it does not go
    /// through one. They are named here, one by one, so adding another is a decision made in this file, and they are held to two rules below:
    /// they name the store, and they write nothing.
    /// </summary>
    private static readonly string[] ReadModels = [nameof(LowStockRepository)];

    private static List<Type> StoreOwnedEntities() =>
        [.. Domain.GetTypes()
            .Where(type => type.IsClass && string.Equals(type.Namespace, "GrocerEase.Domain.Entities", StringComparison.Ordinal)
                && type.GetProperty("StoreId", typeof(Guid)) is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    private static List<Type> RepositoryInterfaces() =>
        [.. Application.GetTypes()
            .Where(type => type.IsInterface
                && string.Equals(type.Namespace, "GrocerEase.Application.Abstractions", StringComparison.Ordinal)
                && type.Name.EndsWith("Repository", StringComparison.Ordinal))];

    // Task<Product?>, Task<PagedResult<Product>>, IReadOnlyList<Product>: the entity is somewhere inside the type.
    private static bool Mentions(Type type, Type entity) =>
        type == entity || (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, entity)));

    /// <summary>
    /// The methods of a repository interface that deal with a store-owned entity and neither take a storeId nor take an entity that
    /// carries one. A method deals with an entity when its signature mentions it, <b>or</b> when the interface is that entity's own
    /// (<c>IInventoryItemRepository</c> for <c>InventoryItem</c>): then every method counts, even one that returns only a number.
    /// An entity that is also owned by a customer (it has a <c>CustomerId</c>, like an order) may be asked for by <c>customerId</c> instead:
    /// a customer reading their own orders is scoped by themselves, and cannot see anyone else's.
    /// </summary>
    private static List<string> UnscopedMethods(Type repository, IReadOnlyCollection<Type> storeOwned) =>
        [.. repository.GetMethods()
            .Where(method => storeOwned.Any(entity =>
                string.Equals(repository.Name, $"I{entity.Name}Repository", StringComparison.Ordinal)
                || Mentions(method.ReturnType, entity)
                || method.GetParameters().Any(parameter => Mentions(parameter.ParameterType, entity))))
            .Where(method => !method.GetParameters().Any(parameter =>
                (parameter.ParameterType == typeof(Guid) && string.Equals(parameter.Name, "storeId", StringComparison.Ordinal))
                || (parameter.ParameterType == typeof(Guid) && string.Equals(parameter.Name, "customerId", StringComparison.Ordinal)
                    && storeOwned.Any(entity => entity.GetProperty("CustomerId", typeof(Guid)) is not null
                        && (string.Equals(repository.Name, $"I{entity.Name}Repository", StringComparison.Ordinal)
                            || Mentions(method.ReturnType, entity))))
                || storeOwned.Contains(parameter.ParameterType)))
            .Select(method => $"{repository.Name}.{method.Name}")];

    [Fact]
    public void EveryRepositoryMethodForStoreOwnedData_TakesTheStoreId_OrAnEntityThatCarriesIt()
    {
        var storeOwned = StoreOwnedEntities();

        var offenders = RepositoryInterfaces().SelectMany(repository => UnscopedMethods(repository, storeOwned)).ToList();

        Assert.True(
            offenders.Count == 0,
            $"A store-owned entity must be asked for in its store: add a storeId parameter to {string.Join(", ", offenders)}");
    }

    // The entity of a store-owned table is read through AppDbContext.<DbSet> (or Set<Entity>()). Reading the IL, not the source, so a call hidden
    // in a lambda or an async method is found too.
    private static List<string> TopLevelTypesThatQuery(Type entity)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);
        var contextType = typeof(AppDbContext).FullName;
        var setProperty = $"get_{typeof(AppDbContext).GetProperties().Single(property =>
            property.PropertyType.IsGenericType && property.PropertyType.GetGenericArguments()[0] == entity).Name}";

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code is Code.Call or Code.Callvirt
                && instruction.Operand is MethodReference called
                && ((string.Equals(called.Name, setProperty, StringComparison.Ordinal)
                        && string.Equals(called.DeclaringType.FullName, contextType, StringComparison.Ordinal))
                    || (string.Equals(called.Name, "Set", StringComparison.Ordinal)
                        && called is GenericInstanceMethod { GenericArguments: [var argument] }
                        && string.Equals(argument.FullName, entity.FullName, StringComparison.Ordinal))))))
            .Select(type => TopLevel(type).Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    // A repository may also change its table with raw SQL (the inventory does, so that a change is one statement), which never touches the
    // DbSet. The SQL names the table, so look for the table's name in the string literals of every Infrastructure type, apart from the
    // mapping (Configurations) and the migrations, which say it by design.
    private static List<string> TopLevelTypesWhoseSqlNames(string table)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            // The namespace of a compiler-generated nested type (a lambda, an async state machine) is empty: ask the top-level type.
            .Where(type => !(TopLevel(type).Namespace ?? string.Empty).EndsWith(".Migrations", StringComparison.Ordinal)
                && !(TopLevel(type).Namespace ?? string.Empty).EndsWith(".Configurations", StringComparison.Ordinal))
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code is Code.Ldstr
                && instruction.Operand is string literal
                && System.Text.RegularExpressions.Regex.IsMatch(literal, $@"\b{table}\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))))
            .Select(type => TopLevel(type).Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    [Theory]
    [InlineData(typeof(Product), "products", nameof(ProductRepository))]
    [InlineData(typeof(StoreMember), "store_members", nameof(StoreMemberRepository))]
    [InlineData(typeof(InventoryItem), "inventory_items", nameof(InventoryItemRepository))]
    [InlineData(typeof(Order), "orders", nameof(OrderRepository))]
    [InlineData(typeof(StockMovement), "stock_movements", nameof(InventoryItemRepository))] // the ledger is part of the stock: one statement writes both
    public void AStoreOwnedTable_IsChangedOnlyByItsRepository_AndReadOnlyByItAndTheNamedReadModels(Type entity, string table, string repository)
    {
        var strangers = TopLevelTypesThatQuery(entity).Union(TopLevelTypesWhoseSqlNames(table), StringComparer.Ordinal)
            .Except([nameof(AppDbContext), repository, .. ReadModels], StringComparer.Ordinal)
            .ToList();

        Assert.True(strangers.Count == 0, $"Only {repository} may change {table} (and, besides the named read models, read it), so every write is scoped by store in one place: {string.Join(", ", strangers)}");
    }

    // What a read model must not do: write. Reading the IL for calls into Entity Framework that change data or run raw SQL (a write can hide in a
    // string). The check is on the type EF methods are declared in, so a List.Add in a read model is not mistaken for a write.
    private static readonly string[] WritingCalls =
    [
        "SaveChanges", "SaveChangesAsync", "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync",
        "ExecuteSql", "ExecuteSqlAsync", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQuery", "SqlQueryRaw", "FromSql", "FromSqlRaw", "FromSqlInterpolated",
        "Add", "AddAsync", "AddRange", "AddRangeAsync", "Remove", "RemoveRange", "Update", "UpdateRange", "Attach",
    ];

    private static List<string> WritingCallsIn(string topLevelTypeName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Infrastructure.Location);

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => string.Equals(TopLevel(type).Name, topLevelTypeName, StringComparison.Ordinal))
            .SelectMany(type => type.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions))
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt && instruction.Operand is MethodReference)
            .Select(instruction => (MethodReference)instruction.Operand)
            .Where(called => called.DeclaringType.FullName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                && WritingCalls.Contains(called.Name, StringComparer.Ordinal))
            .Select(called => called.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    // The ports a read model implements: the repository interfaces of Application.Abstractions that its class implements.
    private static List<Type> PortsOf(string readModel) =>
        [.. Infrastructure.GetTypes().Single(type => string.Equals(type.Name, readModel, StringComparison.Ordinal)).GetInterfaces()
            .Where(port => string.Equals(port.Namespace, "GrocerEase.Application.Abstractions", StringComparison.Ordinal))];

    private static List<string> MethodsWithoutAStoreId(IEnumerable<Type> ports) =>
        [.. ports.SelectMany(port => port.GetMethods())
            .Where(method => !method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(Guid) && string.Equals(parameter.Name, "storeId", StringComparison.Ordinal)))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")];

    [Fact]
    public void ARepositoryThatReadsSeveralStoreOwnedTables_IsANamedReadModel_ThatNamesTheStoreAndWritesNothing()
    {
        var offenders = ReadModels.SelectMany(readModel => PortsOf(readModel).Count == 0
            ? [$"{readModel} implements no port"]
            : MethodsWithoutAStoreId(PortsOf(readModel)).Select(method => $"{method} takes no storeId")
                .Concat(WritingCallsIn(readModel).Select(call => $"{readModel} calls {call}"))).ToList();

        Assert.True(offenders.Count == 0, $"A read model may join store-owned tables only if it names the store and writes nothing: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void EveryNamedReadModel_ReallyJoinsStoreOwnedTables()
    {
        // The list is closed: any other class that reads a store-owned table would already be flagged by the rule above. This pins the other
        // direction: each read model really does read several of the tables, so it is a read model and not a repository that could live with its table.
        foreach (var readModel in ReadModels)
        {
            var tables = new[] { typeof(InventoryItem), typeof(Product) }.Where(entity => TopLevelTypesThatQuery(entity).Contains(readModel, StringComparer.Ordinal)).ToList();

            Assert.True(tables.Count >= 2, $"{readModel} is listed as a read model but reads {tables.Count} of the tables: a read model that reads one table belongs in that table's repository.");
        }
    }

    private interface IBlindReadModel
    {
        Task<int> CountAsync(int page, CancellationToken cancellationToken); // forgot the store
    }

    [Fact]
    public void TheReadModelRules_CanFail_BecauseTheyCatchAPortWithoutAStoreId_AndSeeWritesWhereThereAreSome()
    {
        Assert.Equal(["IBlindReadModel.CountAsync"], MethodsWithoutAStoreId([typeof(IBlindReadModel)]), StringComparer.Ordinal);
        Assert.Empty(MethodsWithoutAStoreId(PortsOf(nameof(LowStockRepository))));
        // The same scanner sees the writes of the repositories that write, so an empty answer for the read model means something.
        Assert.Contains("ExecuteUpdateAsync", WritingCallsIn(nameof(OrderRepository)), StringComparer.Ordinal);
        Assert.Contains("SqlQuery", WritingCallsIn(nameof(InventoryItemRepository)), StringComparer.Ordinal);
        Assert.Contains("SaveChangesAsync", WritingCallsIn(nameof(ProductRepository)), StringComparer.Ordinal);
        Assert.Empty(WritingCallsIn(nameof(LowStockRepository)));
    }

    [Fact]
    public void EveryStoreOwnedEntity_HasATestedRepositoryRule()
    {
        // A new store-owned entity (an order, an inventory item) must be added to the theory above, or its table would be unguarded.
        var covered = new[] { typeof(InventoryItem), typeof(Order), typeof(Product), typeof(StockMovement), typeof(StoreMember) };

        Assert.Equal(StoreOwnedEntities().Select(type => type.Name), covered.Select(type => type.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private interface IUnscopedRepository
    {
        Task<Product?> GetAsync(Guid productId, CancellationToken cancellationToken); // forgot the store
    }

    // The entity's own repository: even a method that returns only a number must name the store.
    private interface IInventoryItemRepository
    {
        Task<int?> CountAsync(Guid productId, CancellationToken cancellationToken); // forgot the store, and mentions no entity

        Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken);
    }

    // An order is also a customer's: a read by customerId is scoped, a read by nothing is not.
    private interface IOrderRepository
    {
        Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken);

        Task<Order?> GetAnyAsync(Guid orderId, CancellationToken cancellationToken); // forgot both the store and the customer
    }

    private interface IScopedRepository
    {
        Task<Product?> GetAsync(Guid storeId, Guid productId, CancellationToken cancellationToken);

        Task<bool> AddAsync(Product product, CancellationToken cancellationToken); // the product carries its store
    }

    [Fact]
    public void TheRule_CanFail_BecauseItSeesStoreOwnedEntitiesAndCatchesAMethodThatForgetsTheStore()
    {
        var storeOwned = StoreOwnedEntities();

        Assert.Contains(typeof(Product), storeOwned);
        Assert.Contains(typeof(StoreMember), storeOwned);
        Assert.DoesNotContain(typeof(Store), storeOwned); // a store is not owned by a store
        Assert.Equal(["IUnscopedRepository.GetAsync"], UnscopedMethods(typeof(IUnscopedRepository), storeOwned), StringComparer.Ordinal);
        Assert.Empty(UnscopedMethods(typeof(IScopedRepository), storeOwned));
        Assert.Equal(["IInventoryItemRepository.CountAsync"], UnscopedMethods(typeof(IInventoryItemRepository), storeOwned), StringComparer.Ordinal);
        Assert.Equal(["IOrderRepository.GetAnyAsync"], UnscopedMethods(typeof(IOrderRepository), storeOwned), StringComparer.Ordinal);
        Assert.Null(typeof(Product).GetProperty("CustomerId")); // customerId is a scope only for entities that have one: a product is not a customer's
        Assert.Contains(typeof(IProductRepository), RepositoryInterfaces()); // and the real one is among those checked
    }

    [Fact]
    public void TheQueryRule_CanFail_BecauseTheRepositoriesDoReachTheirTables()
    {
        // Guards the rule above against passing by accident: a scanner that sees nothing passes anything.
        Assert.Contains(TopLevelTypesThatQuery(typeof(Product)), name => string.Equals(name, nameof(ProductRepository), StringComparison.Ordinal));
        Assert.Contains(TopLevelTypesThatQuery(typeof(StoreMember)), name => string.Equals(name, nameof(StoreMemberRepository), StringComparison.Ordinal));
        Assert.Contains(TopLevelTypesWhoseSqlNames("inventory_items"), name => string.Equals(name, nameof(InventoryItemRepository), StringComparison.Ordinal));
    }
}
