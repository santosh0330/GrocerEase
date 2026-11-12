using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
using GrocerEase.Application.Roles.Common;
using GrocerEase.Application.Roles.Handlers;
using GrocerEase.Application.Roles.Queries;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Handlers;
using GrocerEase.Application.Stores.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the validators and the command handlers. Api calls this once, so adding a use case means editing this
    /// file, not the composition root. The ports these handlers need are registered by <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Every AbstractValidator<T> in this assembly, so a new validator needs no registration line.
        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);

        services.AddScoped<RefreshTokenIssuer>(); // scoped, like the repository it writes through

        services.AddValidatedCommandHandler<RegisterCommand, Result<AuthenticationResult>, RegisterCommandHandler>();
        services.AddValidatedCommandHandler<LoginCommand, Result<AuthenticationResult>, LoginCommandHandler>();
        services.AddValidatedCommandHandler<RefreshCommand, Result<AuthenticationResult>, RefreshCommandHandler>();
        services.AddValidatedCommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>, ListRolesQueryHandler>();
        services.AddValidatedCommandHandler<CreateStoreCommand, Result<StoreResult>, CreateStoreCommandHandler>();
        services.AddValidatedCommandHandler<GetStoreQuery, Result<StoreResult>, GetStoreQueryHandler>();
        services.AddValidatedCommandHandler<ListStoresQuery, Result<IReadOnlyList<StoreResult>>, ListStoresQueryHandler>();

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> like <c>AddValidatedCommandHandler</c>, and also runs it in a unit of work
    /// (<see cref="TransactionDecorator{TCommand, TResponse}"/>): what it writes is kept together or not at all. The validation
    /// is the outer wrapper, so a command that fails it is refused before any transaction is opened.
    /// </summary>
    public static IServiceCollection AddTransactionalCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResponse>
        where TResponse : IResult
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider => new ValidationDecorator<TCommand, TResponse>(
            new TransactionDecorator<TCommand, TResponse>(provider.GetRequiredService<THandler>(), provider.GetRequiredService<IUnitOfWork>()),
            provider.GetServices<IValidator<TCommand>>()));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> for its command and wraps it in a <see cref="ValidationDecorator{TCommand, TResponse}"/>,
    /// so callers asking for <c>ICommandHandler&lt;TCommand, TResponse&gt;</c> get the validated version.
    /// </summary>
    private static void AddValidatedCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider => new ValidationDecorator<TCommand, TResponse>(
            provider.GetRequiredService<THandler>(),
            provider.GetServices<IValidator<TCommand>>()));
    }
}
