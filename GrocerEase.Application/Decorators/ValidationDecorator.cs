using FluentValidation;
using FluentValidation.Results;
using GrocerEase.Application.Abstractions;

namespace GrocerEase.Application.Decorators;

/// <summary>
/// Wraps a handler: runs every validator for the command first and throws
/// <see cref="ValidationException"/> if any rule fails, so the handler only ever sees a valid command.
/// </summary>
public sealed class ValidationDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            // A fresh validation each time: a shared ValidationContext accumulates failures and would repeat them.
            var result = await validator.ValidateAsync(command, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await inner.Handle(command, cancellationToken);
    }
}
