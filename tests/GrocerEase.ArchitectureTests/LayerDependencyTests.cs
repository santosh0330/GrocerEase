using System.Reflection;
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
