using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Handlers;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Handlers;
using GrocerEase.Application.Products.Queries;
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
        services.AddScoped<StoreAccess>(); // the ownership rule, scoped like the repository it reads through

        services.AddValidatedCommandHandler<RegisterCommand, Result<AuthenticationResult>, RegisterCommandHandler>();
        services.AddValidatedCommandHandler<LoginCommand, Result<AuthenticationResult>, LoginCommandHandler>();
        services.AddValidatedCommandHandler<RefreshCommand, Result<AuthenticationResult>, RefreshCommandHandler>();
        services.AddValidatedCommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>, ListRolesQueryHandler>();
        services.AddValidatedCommandHandler<CreateStoreCommand, Result<StoreResult>, CreateStoreCommandHandler>();
        services.AddValidatedCommandHandler<GetStoreQuery, Result<StoreResult>, GetStoreQueryHandler>();
        services.AddValidatedCommandHandler<ListStoresQuery, Result<PagedResult<StoreResult>>, ListStoresQueryHandler>();

        services.AddValidatedCommandHandler<CreateProductCommand, Result<ProductResult>, CreateProductCommandHandler>();
        services.AddValidatedCommandHandler<GetProductQuery, Result<ProductResult>, GetProductQueryHandler>();
        services.AddValidatedCommandHandler<UpdateProductCommand, Result<ProductResult>, UpdateProductCommandHandler>();
        services.AddValidatedCommandHandler<ArchiveProductCommand, Result<ProductResult>, ArchiveProductCommandHandler>();
        services.AddValidatedCommandHandler<ListProductsQuery, Result<PagedResult<ProductResult>>, ListProductsQueryHandler>();

        services.AddValidatedCommandHandler<AdjustStockCommand, Result<StockResult>, AdjustStockCommandHandler>();
        services.AddValidatedCommandHandler<ListInventoryQuery, Result<PagedResult<InventoryEntryResult>>, ListInventoryQueryHandler>();

        services.AddValidatedCommandHandler<GetMyOrderQuery, Result<OrderResult>, GetMyOrderQueryHandler>();
        services.AddValidatedCommandHandler<ListMyOrdersQuery, Result<PagedResult<OrderResult>>, ListMyOrdersQueryHandler>();
        services.AddValidatedCommandHandler<ListStoreOrdersQuery, Result<PagedResult<OrderResult>>, ListStoreOrdersQueryHandler>();

        // Reserves the stock of every line and writes the order, which must be kept together or not at all.
        services.AddTransactionalCommandHandler<PlaceOrderCommand, Result<OrderResult>, PlaceOrderCommandHandler>();

        // Writes a membership and a role, which must be kept together or not at all.
        services.AddTransactionalCommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>, AssignStoreMemberCommandHandler>();
        services.AddTransactionalCommandHandler<RemoveStoreMemberCommand, Result<StoreMemberResult>, RemoveStoreMemberCommandHandler>();

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
