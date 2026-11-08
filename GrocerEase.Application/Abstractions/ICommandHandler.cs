namespace GrocerEase.Application.Abstractions;

/// <summary>Runs one use case: takes a command, returns what the caller needs.</summary>
public interface ICommandHandler<in TCommand, TResponse>
{
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}
