using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;

namespace GrocerEase.Application.Decorators;

/// <summary>
/// Wraps a handler in a unit of work: everything the handler writes is kept if it returns a success, and undone if it returns a
/// failure or throws. A handler that makes several writes, say a role change and a membership, is therefore all or nothing, and
/// can simply return a <see cref="ResultError"/> when the second step cannot be done, with no code of its own to undo the first.
/// </summary>
/// <remarks>
/// Opt in, one handler at a time, with <c>AddTransactionalCommandHandler</c>. It is not applied to every handler: a handler that
/// writes once gains nothing, and one that uses a repository that opens its own transaction cannot run inside a unit at all.
/// </remarks>
public sealed class TransactionDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IUnitOfWork unitOfWork) : ICommandHandler<TCommand, TResponse>
    where TResponse : IResult
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Disposed without a commit on every path except the one that reaches CommitAsync: an exception, or a returned failure.
        await using var unit = await unitOfWork.BeginAsync(cancellationToken);

        var response = await inner.Handle(command, cancellationToken);
        if (response.IsSuccess)
        {
            await unit.CommitAsync(cancellationToken);
        }

        return response;
    }
}
