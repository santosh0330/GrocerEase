using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: a user already in a store is refused.</summary>
internal sealed class FakeStoreMemberRepository : IStoreMemberRepository
{
    public List<StoreMember> Members { get; } = [];

    public Task<bool> TryAddAsync(StoreMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (Members.Exists(existing => existing.UserId == member.UserId))
        {
            return Task.FromResult(false);
        }

        Members.Add(member);
        return Task.FromResult(true);
    }

    public Task<bool> IsMemberAsync(Guid storeId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.Exists(member => member.StoreId == storeId && member.UserId == userId));

    public Task<bool> TryRemoveAsync(Guid storeId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.RemoveAll(member => member.StoreId == storeId && member.UserId == userId) == 1);
}
