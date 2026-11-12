namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Makes several writes one: whatever happens between <see cref="BeginAsync"/> and <see cref="IUnitOfWorkTransaction.CommitAsync"/> is
/// kept together or thrown away together. Application owns the interface and Infrastructure implements it, so a handler never knows
/// that the unit is a database transaction.
/// </summary>
/// <remarks>
/// Scoped, like the repositories: they all write through the same context, so a unit begun here covers every one of them. A unit
/// cannot be begun inside another one; the second call throws instead of quietly joining it.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Opens a unit. Dispose it to end it: without a <see cref="IUnitOfWorkTransaction.CommitAsync"/> first, everything written since is undone.</summary>
    Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken);
}
