using System.Reflection;
using GrocerEase.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// Enforces the clean architecture dependency direction:
/// Api -> Application/Infrastructure/Contracts, Infrastructure -> Application, Application -> Domain.
/// </summary>
public class LayerDependencyTests
{
    private const string DomainNs = "GrocerEase.Domain";
    private const string ApplicationNs = "GrocerEase.Application";
    private const string InfrastructureNs = "GrocerEase.Infrastructure";
    private const string ContractsNs = "GrocerEase.Contracts";
    private const string ApiNs = "GrocerEase.Api";

    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Contracts = typeof(Contracts.AssemblyReference).Assembly;

    [Fact]
    public void Domain_DoesNotDependOnAnyOtherLayer() =>
        AssertNoDependencies(Domain, ApplicationNs, InfrastructureNs, ContractsNs, ApiNs);

    [Fact]
    public void Application_DoesNotDependOnInfrastructureContractsOrApi() =>
        AssertNoDependencies(Application, InfrastructureNs, ContractsNs, ApiNs);

    [Fact]
    public void Infrastructure_DoesNotDependOnContractsOrApi() =>
        AssertNoDependencies(Infrastructure, ContractsNs, ApiNs);

    [Fact]
    public void Contracts_DoesNotDependOnAnyOtherLayer() =>
        AssertNoDependencies(Contracts, DomainNs, ApplicationNs, InfrastructureNs, ApiNs);

    [Fact]
    public void Contracts_RequestsAndResponses_AreSealedRecords()
    {
        var types = Types.InAssembly(Contracts)
            .That().ResideInNamespaceStartingWith($"{ContractsNs}.")
            .And().HaveNameEndingWith("Request", StringComparison.Ordinal).Or().HaveNameEndingWith("Response", StringComparison.Ordinal)
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        // A record has a compiler-generated <Clone>$ method; a class does not.
        var notRecords = types.Where(t => t.GetMethod("<Clone>$") is null || !t.IsSealed).Select(t => t.Name);
        Assert.True(!notRecords.Any(), $"Request/response types must be sealed records: {string.Join(", ", notRecords)}");
    }

    [Fact]
    public void Controllers_AreSealedControllerBasesNamedController_AndSkipDomainAndInfrastructure()
    {
        var controllers = Types.InAssembly(Api).That().ResideInNamespace($"{ApiNs}.Controllers");

        Assert.NotEmpty(controllers.GetTypes());

        var shape = controllers.Should().HaveNameEndingWith("Controller", StringComparison.Ordinal)
            .And().Inherit(typeof(ControllerBase))
            .And().BeSealed()
            .GetResult();
        Assert.True(shape.IsSuccessful, $"Offending controllers: {string.Join(", ", shape.FailingTypeNames ?? [])}");

        var dependencies = controllers.ShouldNot().HaveDependencyOnAny(DomainNs, InfrastructureNs).GetResult();
        Assert.True(
            dependencies.IsSuccessful,
            $"Controllers must go through Application, not Domain/Infrastructure: {string.Join(", ", dependencies.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_Commands_AreSealedRecords()
    {
        var types = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Command", StringComparison.Ordinal)
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        var notRecords = types.Where(t => t.GetMethod("<Clone>$") is null || !t.IsSealed).Select(t => t.Name);
        Assert.True(!notRecords.Any(), $"Commands must be sealed records: {string.Join(", ", notRecords)}");
    }

    [Fact]
    public void Application_Handlers_AreSealedCommandHandlers()
    {
        var types = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .And().AreClasses()
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        var offenders = types
            .Where(t => !t.IsSealed || !t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(t => t.Name);
        Assert.True(!offenders.Any(), $"Handlers must be sealed and implement ICommandHandler<,>: {string.Join(", ", offenders)}");
    }

    private static void AssertNoDependencies(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on [{string.Join(", ", forbidden)}]. " +
            $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
