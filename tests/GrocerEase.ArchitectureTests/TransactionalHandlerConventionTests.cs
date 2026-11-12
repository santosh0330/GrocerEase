using System.Reflection;
using GrocerEase.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// What a transactional handler may depend on. A handler registered with <c>AddTransactionalCommandHandler</c> runs inside a unit of
/// work, and EF Core does not nest transactions, so a repository that begins its own cannot be called from one: it throws on the first
/// call, in whatever request first reaches it. <see cref="TransactionConventionTests"/> names the types that begin transactions;
/// this says no transactional handler may depend on the port such a type implements. (It is not "every handler that writes twice must
/// be transactional": <c>RegisterCommandHandler</c> writes a user and a session and is not yet, which is a known gap, not a rule.)
/// </summary>
public class TransactionalHandlerConventionTests
{
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    // Reads the IL of Application's registrations: a call to AddTransactionalCommandHandler<TCommand, TResponse, THandler> names its
    // handler as the third type argument.
    private static List<Type> TransactionalHandlers()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Application.Location);

        return [.. assembly.MainModule.GetTypes()
            .SelectMany(type => type.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions))
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt
                && instruction.Operand is GenericInstanceMethod { Name: "AddTransactionalCommandHandler" })
            .Select(instruction => ((GenericInstanceMethod)instruction.Operand).GenericArguments[2].FullName)
            .Distinct(StringComparer.Ordinal)
            .Select(name => Application.GetType(name) ?? throw new InvalidOperationException($"Handler type '{name}' was not found."))
            .OrderBy(type => type.Name, StringComparer.Ordinal)];
    }

    // The ports implemented by a type that begins a transaction of its own (the unit of work itself does not count: it is the transaction).
    private static List<Type> PortsOfRepositoriesThatBeginTheirOwnTransaction()
    {
        var beginners = TransactionConventionTests.TopLevelTypesThatBeginTransactions()
            .Where(name => !string.Equals(name, nameof(EfUnitOfWork), StringComparison.Ordinal))
            .ToList();

        return [.. Infrastructure.GetTypes()
            .Where(type => type.IsClass && beginners.Contains(type.Name, StringComparer.Ordinal))
            .SelectMany(type => type.GetInterfaces())
            .Where(port => string.Equals(port.Namespace, "GrocerEase.Application.Abstractions", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(port => port.Name, StringComparer.Ordinal)];
    }

    [Fact]
    public void ATransactionalHandler_DoesNotDependOnARepositoryThatBeginsItsOwnTransaction()
    {
        var forbidden = PortsOfRepositoriesThatBeginTheirOwnTransaction();

        var offenders = TransactionalHandlers()
            .SelectMany(handler => handler.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => forbidden.Contains(parameter.ParameterType))
                .Select(parameter => $"{handler.Name} takes {parameter.ParameterType.Name}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "EF Core does not nest transactions, so these cannot run inside a unit of work. Make the repository join the unit of work, or "
            + $"register the handler without AddTransactionalCommandHandler: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheRule_CanFail_BecauseItSeesTheTransactionalHandlersAndThePortThatCannotJoin()
    {
        // Guards the test above against passing by accident: a scanner that finds no handlers, or no forbidden port, passes anything.
        var handlers = TransactionalHandlers().Select(type => type.Name).ToList();
        var ports = PortsOfRepositoriesThatBeginTheirOwnTransaction().Select(type => type.Name).ToList();

        Assert.Contains("AssignStoreMemberCommandHandler", handlers, StringComparer.Ordinal);
        Assert.Contains("RemoveStoreMemberCommandHandler", handlers, StringComparer.Ordinal);
        Assert.Equal(["IRefreshTokenRepository"], ports);
    }

    [Fact]
    public void ARegistrationThatDependsOnTheForbiddenPort_WouldBeCaught()
    {
        // The same check the rule makes, on a handler that does depend on it: proves the comparison is not vacuous.
        var forbidden = PortsOfRepositoriesThatBeginTheirOwnTransaction();
        var refreshHandler = Application.GetType("GrocerEase.Application.Authentication.Handlers.RefreshCommandHandler")!;

        Assert.Contains(
            refreshHandler.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => forbidden.Contains(parameter.ParameterType));
    }
}
