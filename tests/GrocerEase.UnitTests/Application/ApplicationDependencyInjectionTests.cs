using FluentValidation;
using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
using GrocerEase.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.UnitTests.Application;

public class ApplicationDependencyInjectionTests
{
    // The handlers need the ports, which AddInfrastructure registers; the real wiring is what Program.cs does.
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=never_connected",
                ["Jwt:SecretKey"] = "unit-test-signing-key-0123456789-abcdef",
                ["Jwt:Issuer"] = "issuer-x",
                ["Jwt:Audience"] = "audience-x",
                ["Jwt:ExpiryMinutes"] = "15",
            })
            .Build();
        return new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddApplication_ReturnsTheSameCollection_SoCallsCanBeChained()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddApplication());
    }

    [Fact]
    public void TheHandlers_AreWrappedInAValidationDecorator()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.IsType<ValidationDecorator<RegisterCommand, Result<AuthenticationResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterCommand, Result<AuthenticationResult>>>());
        Assert.IsType<ValidationDecorator<LoginCommand, Result<AuthenticationResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>());
    }

    [Fact]
    public async Task AnInvalidCommand_IsStoppedByTheValidatorsBeforeTheHandlerRuns()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterCommand, Result<AuthenticationResult>>>();

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new RegisterCommand("", "", "", ""), CancellationToken.None));
    }

    [Fact]
    public void EveryCommandsValidator_IsRegistered()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<RegisterCommand>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<LoginCommand>>());
    }

    [Fact]
    public void TheHandlers_AreScoped_OneInstancePerRequest()
    {
        using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>();

        Assert.Same(inFirst, first.ServiceProvider.GetRequiredService<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>());
        Assert.NotSame(inFirst, second.ServiceProvider.GetRequiredService<ICommandHandler<LoginCommand, Result<AuthenticationResult>>>());
    }

    [Fact]
    public void AddApplication_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddApplication());
}
