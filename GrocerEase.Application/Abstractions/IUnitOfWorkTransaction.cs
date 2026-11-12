namespace GrocerEase.Application.Abstractions;

/// <summary>
/// One open unit of work. Disposing it without committing undoes the writes, so an exception, a returned failure or a forgotten
/// commit all end the same safe way: nothing is kept.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Keeps everything written since the unit began.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
