using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Decorators;

namespace GrocerEase.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> for its command and wraps it in a <see cref="ValidationDecorator{TCommand, TResponse}"/>,
    /// so callers asking for <c>ICommandHandler&lt;TCommand, TResponse&gt;</c> get the validated version.
    /// </summary>
    public static IServiceCollection AddValidatedCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider => new ValidationDecorator<TCommand, TResponse>(
            provider.GetRequiredService<THandler>(),
            provider.GetServices<IValidator<TCommand>>()));
        return services;
    }
}
