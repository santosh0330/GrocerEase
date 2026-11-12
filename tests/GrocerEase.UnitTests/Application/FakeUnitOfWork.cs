using GrocerEase.Application.Abstractions;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// A unit of work that only keeps a diary, standing in for the database in decorator tests. The diary says what happened and in
/// which order: <c>begin</c>, then <c>commit</c> or <c>rollback</c> (a unit disposed without a commit), and what the handler did
/// in between, which the test writes into the same list.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public List<string> Events { get; } = [];

    public Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        Events.Add("begin");
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(Events));
    }

    private sealed class Transaction(List<string> events) : IUnitOfWorkTransaction
    {
        private bool _committed;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            _committed = true;
            events.Add("commit");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                events.Add("rollback");
            }

            return ValueTask.CompletedTask;
        }
    }
}
