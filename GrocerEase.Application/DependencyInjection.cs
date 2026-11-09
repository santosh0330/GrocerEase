using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
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

        services.AddValidatedCommandHandler<RegisterCommand, Result<AuthenticationResult>, RegisterCommandHandler>();
        services.AddValidatedCommandHandler<LoginCommand, Result<AuthenticationResult>, LoginCommandHandler>();

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
