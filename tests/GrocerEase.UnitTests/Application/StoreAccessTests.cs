using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class StoreAccessTests
{
    private readonly FakeStoreMemberRepository _members = new();

    [Fact]
    public async Task AMember_MayTouchTheirStore()
    {
        var (storeId, userId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _members.TryAddAsync(new StoreMember(Guid.CreateVersion7(), storeId, userId), CancellationToken.None);

        Assert.Null(await new StoreAccess(_members).RequireMemberAsync(storeId, userId, CancellationToken.None));
    }

    [Fact]
    public async Task ANonMember_GetsNotFound_ForAStoreThatExistsAndForOneThatDoesNot_AlikeSoNothingLeaks()
    {
        var theirStore = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await _members.TryAddAsync(new StoreMember(Guid.CreateVersion7(), theirStore, user), CancellationToken.None);
        var access = new StoreAccess(_members);

        var otherStore = await access.RequireMemberAsync(Guid.CreateVersion7(), user, CancellationToken.None); // may exist, may not: the answer is one
        var someoneElse = await access.RequireMemberAsync(theirStore, Guid.CreateVersion7(), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, otherStore);
        Assert.Same(StoreErrors.NotFound, someoneElse);
    }
}
