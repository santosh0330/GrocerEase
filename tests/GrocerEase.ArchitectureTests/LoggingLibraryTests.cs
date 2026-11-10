using System.Reflection;
using NetArchTest.Rules;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// Where the logging library may be used. Serilog decides where events go (console, files, Seq) and in what shape, which is wiring, so
/// only the composition root, Api, knows it. Everything else writes to <c>ILogger&lt;T&gt;</c>, the standard abstraction. Replacing
/// Serilog, or adding a sink, then never touches business code, and no layer can start to depend on how events are stored.
/// </summary>
public class LoggingLibraryTests
{
    private const string Serilog = "Serilog";

    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly Contracts = typeof(Contracts.AssemblyReference).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void AssertDoesNotUseSerilog(Assembly assembly)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(Serilog).GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must log through Microsoft.Extensions.Logging (ILogger<T>), not Serilog. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_DoesNotUseSerilog() => AssertDoesNotUseSerilog(Domain);

    [Fact]
    public void Application_DoesNotUseSerilog() => AssertDoesNotUseSerilog(Application);

    [Fact]
    public void Infrastructure_DoesNotUseSerilog() => AssertDoesNotUseSerilog(Infrastructure);

    [Fact]
    public void Contracts_DoesNotUseSerilog() => AssertDoesNotUseSerilog(Contracts);

    [Fact]
    public void TheSerilogRule_CanFail_BecauseApiDoesUseSerilog()
    {
        // Guards the four tests above against passing by accident (a wrong namespace would match nothing).
        var result = Types.InAssembly(Api).ShouldNot().HaveDependencyOn(Serilog).GetResult();

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void InApi_SerilogIsUsedOnlyInTheLoggingFolder()
    {
        // Wiring is in one folder: a controller or a handler that used Serilog directly would be logging for one sink in particular.
        var result = Types.InAssembly(Api)
            .That().DoNotResideInNamespaceStartingWith("GrocerEase.Api.Logging")
            .ShouldNot().HaveDependencyOn(Serilog)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Serilog may only be used in GrocerEase.Api.Logging. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
