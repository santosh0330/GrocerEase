using System.Reflection;
using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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

    // Hashing and tokens are details behind IPasswordHasher and IJwtTokenGenerator. If Domain or Application used these
    // libraries directly, swapping the algorithm or the library would mean changing business code.
    private static readonly string[] IdentityAndTokenLibraries =
    [
        "Microsoft.AspNetCore.Identity",
        "Microsoft.Extensions.Identity",
        "Microsoft.IdentityModel",
        "System.IdentityModel",
    ];

    [Fact]
    public void Domain_DoesNotUseIdentityOrTokenLibraries() =>
        AssertNoDependencies(Domain, IdentityAndTokenLibraries);

    [Fact]
    public void Application_DoesNotUseIdentityOrTokenLibraries() =>
        AssertNoDependencies(Application, IdentityAndTokenLibraries);

    [Fact]
    public void TheIdentityAndTokenLibraryRule_CanFail_BecauseInfrastructureDoesUseThem()
    {
        // Guards the two tests above against passing by accident (a wrong namespace would match nothing).
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOnAny(IdentityAndTokenLibraries).GetResult();

        Assert.False(result.IsSuccessful);
    }

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

        var misplaced = types.Where(t => t.Namespace is null || !t.Namespace.EndsWith(".Handlers", StringComparison.Ordinal)).Select(t => t.FullName);
        Assert.True(!misplaced.Any(), $"Handlers must live in a Handlers namespace/folder: {string.Join(", ", misplaced)}");
    }

    [Fact]
    public void Application_Handlers_ReturnAResult()
    {
        var types = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .And().AreClasses()
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        // The response type of ICommandHandler<TCommand, TResponse> must be Result<T>, so a failure cannot be forgotten.
        var offenders = types
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                && i.GetGenericArguments()[1] is { IsGenericType: true } response
                && response.GetGenericTypeDefinition() == typeof(Result<>)))
            .Select(t => t.Name);
        Assert.True(!offenders.Any(), $"Handlers must return Result<T>: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Application_Validators_AreSealedAbstractValidatorsInAValidatorsFolder()
    {
        var types = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Validator", StringComparison.Ordinal)
            .And().AreClasses()
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        var offenders = types
            .Where(t => !t.IsSealed
                || !InheritsFromGeneric(t, typeof(AbstractValidator<>))
                || t.Namespace is null
                || !t.Namespace.EndsWith(".Validators", StringComparison.Ordinal))
            .Select(t => t.FullName);
        Assert.True(!offenders.Any(), $"Validators must be sealed AbstractValidator<> classes in a .Validators namespace: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Application_EveryCommand_HasAValidator()
    {
        var commands = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Command", StringComparison.Ordinal)
            .GetTypes()
            .ToList();
        var validators = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Validator", StringComparison.Ordinal)
            .GetTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(commands);
        var missing = commands.Where(c => !validators.Contains(c.Name + "Validator")).Select(c => c.Name);
        Assert.True(!missing.Any(), $"Commands without a <Command>Validator: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Application_Abstractions_AreInterfacesNamedWithAnI()
    {
        var types = Types.InAssembly(Application)
            .That().ResideInNamespace($"{ApplicationNs}.Abstractions")
            .GetTypes()
            .ToList();

        Assert.NotEmpty(types);
        var offenders = types
            .Where(t => !t.IsInterface || !t.Name.StartsWith('I'))
            .Select(t => t.Name);
        Assert.True(!offenders.Any(), $"Application.Abstractions must hold only interfaces named I...: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Infrastructure_ImplementsEveryApplicationPort_WithASealedClass()
    {
        // ICommandHandler is implemented by Application's own handlers; every other abstraction is a port
        // that Application needs and Infrastructure must provide.
        var ports = Types.InAssembly(Application)
            .That().ResideInNamespace($"{ApplicationNs}.Abstractions")
            .And().AreInterfaces()
            .GetTypes()
            .Where(t => t != typeof(ICommandHandler<,>))
            .ToList();
        var infrastructureClasses = Types.InAssembly(Infrastructure).That().AreClasses().GetTypes().ToList();

        Assert.NotEmpty(ports);
        var unimplemented = ports
            .Where(port => !infrastructureClasses.Any(c => port.IsAssignableFrom(c)))
            .Select(port => port.Name);
        Assert.True(!unimplemented.Any(), $"Ports without an Infrastructure implementation: {string.Join(", ", unimplemented)}");

        var notSealed = infrastructureClasses
            .Where(c => ports.Any(port => port.IsAssignableFrom(c)) && !c.IsSealed)
            .Select(c => c.Name);
        Assert.True(!notSealed.Any(), $"Infrastructure implementations must be sealed: {string.Join(", ", notSealed)}");
    }

    [Theory]
    [InlineData(ApplicationNs, "AddApplication")]
    [InlineData(InfrastructureNs, "AddInfrastructure")]
    public void EachLayerRegistersItself_ThroughAPublicAddExtensionOnIServiceCollection(string layerNamespace, string methodName)
    {
        var assembly = string.Equals(layerNamespace, ApplicationNs, StringComparison.Ordinal) ? Application : Infrastructure;

        var type = assembly.GetType($"{layerNamespace}.DependencyInjection");

        Assert.NotNull(type);
        Assert.True(type.IsAbstract && type.IsSealed && type.IsPublic, $"{type.FullName} must be a public static class.");
        var method = type.GetMethods().SingleOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));
        Assert.NotNull(method);
        Assert.True(method.IsStatic && method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false), $"{methodName} must be an extension method.");
        Assert.Equal(typeof(IServiceCollection), method.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IServiceCollection), method.ReturnType);
    }

    private static bool InheritsFromGeneric(Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
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
