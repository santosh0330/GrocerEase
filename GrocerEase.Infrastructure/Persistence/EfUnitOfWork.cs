using GrocerEase.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// The unit of work as a database transaction on the request's <see cref="AppDbContext"/>. Scoped, like the context: every
/// repository of the request writes through that one context, so a transaction opened here covers all of them, whether they
/// save with <c>SaveChangesAsync</c> or run a statement of their own (<c>ExecuteUpdateAsync</c>). They need no change to join it.
/// </summary>
/// <remarks>
/// EF Core does not nest transactions: a second <c>BeginTransactionAsync</c> on a context that already has one throws. That is
/// why a repository that opens a transaction of its own (see <see cref="RefreshTokenRepository"/>) cannot run inside a unit of
/// work, and why this class says so clearly instead of leaving the caller with EF's message.
/// </remarks>
public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A unit of work is already open on this request. They do not nest: a handler is wrapped once, and a repository " +
                "that opens its own transaction cannot run inside one.");
        }

        return new EfUnitOfWorkTransaction(await db.Database.BeginTransactionAsync(cancellationToken));
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        // Disposing an uncommitted transaction rolls it back, which is the whole safety net of the unit of work.
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
