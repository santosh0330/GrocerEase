using FluentValidation;
using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Application.Roles.Common;
using GrocerEase.Application.Roles.Queries;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Queries;
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
                ["RefreshToken:LifetimeDays"] = "3",
                ["RefreshToken:MaxLifetimeDays"] = "30",
            })
            .Build();
        return new ServiceCollection()
            .AddLogging() // the web host provides it; the refresh token repository logs
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
        Assert.IsType<ValidationDecorator<RefreshCommand, Result<AuthenticationResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshCommand, Result<AuthenticationResult>>>());
        Assert.IsType<ValidationDecorator<ListRolesQuery, Result<IReadOnlyList<RoleResult>>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>>>());
        Assert.IsType<ValidationDecorator<CreateStoreCommand, Result<StoreResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateStoreCommand, Result<StoreResult>>>());
        Assert.IsType<ValidationDecorator<GetStoreQuery, Result<StoreResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<GetStoreQuery, Result<StoreResult>>>());
        Assert.IsType<ValidationDecorator<ListStoresQuery, Result<PagedResult<StoreResult>>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ListStoresQuery, Result<PagedResult<StoreResult>>>>());
        Assert.IsType<ValidationDecorator<CreateProductCommand, Result<ProductResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateProductCommand, Result<ProductResult>>>());
        Assert.IsType<ValidationDecorator<GetProductQuery, Result<ProductResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<GetProductQuery, Result<ProductResult>>>());
        Assert.IsType<ValidationDecorator<UpdateProductCommand, Result<ProductResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateProductCommand, Result<ProductResult>>>());
        Assert.IsType<ValidationDecorator<ArchiveProductCommand, Result<ProductResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ArchiveProductCommand, Result<ProductResult>>>());
        Assert.IsType<ValidationDecorator<ListProductsQuery, Result<PagedResult<ProductResult>>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ListProductsQuery, Result<PagedResult<ProductResult>>>>());
        Assert.IsType<ValidationDecorator<RemoveStoreMemberCommand, Result<StoreMemberResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<RemoveStoreMemberCommand, Result<StoreMemberResult>>>());
        Assert.IsType<ValidationDecorator<AssignStoreMemberCommand, Result<StoreMemberResult>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>>>());
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
        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<RefreshCommand>>());
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
    public void TheRefreshTokenIssuer_IsScoped_BecauseItWritesThroughAScopedRepository()
    {
        using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<RefreshTokenIssuer>();

        Assert.Same(inFirst, first.ServiceProvider.GetRequiredService<RefreshTokenIssuer>());
        Assert.NotSame(inFirst, second.ServiceProvider.GetRequiredService<RefreshTokenIssuer>());
    }

    [Fact]
    public void AddApplication_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddApplication());
}
