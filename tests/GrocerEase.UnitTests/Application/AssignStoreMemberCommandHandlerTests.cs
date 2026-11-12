using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Handlers;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class AssignStoreMemberCommandHandlerTests
{
    // Says "someone else got there first" for the role change, as the real compare-and-set does when the user is no longer a customer.
    private sealed class OvertakenUserRepository(FakeUserRepository inner) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => inner.GetByEmailAsync(email, cancellationToken);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken) => inner.TryAddAsync(user, cancellationToken);

        public Task<bool> TryChangeRoleAsync(Guid userId, Guid fromRoleId, Guid toRoleId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private readonly FakeStoreRepository _stores = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeStoreMemberRepository _members = new();

    private static async Task<User> AddUserAsync(FakeUserRepository users, string email, Guid roleId)
    {
        var user = new User(Guid.CreateVersion7(), "Asha", "Rai", email, "a-password-hash", roleId);
        Assert.True(await users.TryAddAsync(user, CancellationToken.None));
        return user;
    }

    private async Task<Store> AddStoreAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        Assert.True(await _stores.TryAddAsync(store, CancellationToken.None));
        return store;
    }

    private AssignStoreMemberCommandHandler Handler(IUserRepository? users = null) =>
        new(_stores, users ?? _users, new FakeRoleRepository(), _members);

    [Fact]
    public async Task ACustomer_BecomesAStoreUser_AndAMemberOfTheStore()
    {
        var store = await AddStoreAsync();
        var asha = await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);

        var result = await Handler().Handle(new AssignStoreMemberCommand(store.Id, "asha@example.com"), CancellationToken.None);

        Assert.Equal(new StoreMemberResult(store.Id, asha.Id, "Asha", "Rai", "asha@example.com"), result.Value);
        var member = Assert.Single(_members.Members);
        Assert.Equal((store.Id, asha.Id), (member.StoreId, member.UserId));
        Assert.Equal(RoleIds.StoreUser, (await _users.GetByIdAsync(asha.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task TheEmail_IsFoundWhateverItsCaseAndPadding()
    {
        var store = await AddStoreAsync();
        await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);

        var result = await Handler().Handle(new AssignStoreMemberCommand(store.Id, "  ASHA@Example.com "), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AnUnknownStore_IsNotFound_AndNothingIsChanged()
    {
        var asha = await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);

        var result = await Handler().Handle(new AssignStoreMemberCommand(Guid.CreateVersion7(), "asha@example.com"), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Empty(_members.Members);
        Assert.Equal(RoleIds.Customer, (await _users.GetByIdAsync(asha.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task AnUnknownEmail_IsUserNotFound()
    {
        var store = await AddStoreAsync();

        var result = await Handler().Handle(new AssignStoreMemberCommand(store.Id, "nobody@example.com"), CancellationToken.None);

        Assert.Same(StoreErrors.UserNotFound, result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("storeuser")]
    public async Task SomeoneWhoIsNotACustomer_IsRefused_AndKeepsTheirRole(string role)
    {
        var store = await AddStoreAsync();
        var roleId = string.Equals(role, "admin", StringComparison.Ordinal) ? RoleIds.Admin : RoleIds.StoreUser;
        var user = await AddUserAsync(_users, "boss@example.com", roleId);

        var result = await Handler().Handle(new AssignStoreMemberCommand(store.Id, "boss@example.com"), CancellationToken.None);

        Assert.Same(StoreErrors.UserNotACustomer, result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_members.Members);
        Assert.Equal(roleId, (await _users.GetByIdAsync(user.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task ACustomerAnotherAdminAssignedFirst_IsRefusedByTheMembershipIndex()
    {
        var store = await AddStoreAsync();
        var asha = await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);
        await _members.TryAddAsync(new StoreMember(Guid.CreateVersion7(), (await AddStoreAsync()).Id, asha.Id), CancellationToken.None); // the race, lost

        var result = await Handler().Handle(new AssignStoreMemberCommand(store.Id, "asha@example.com"), CancellationToken.None);

        Assert.Same(StoreErrors.UserNotACustomer, result.Error);
        Assert.Single(_members.Members);
    }

    [Fact]
    public async Task WhenTheRoleCannotBeChanged_BecauseSomeoneGotThereFirst_TheAnswerIsNotACustomer()
    {
        var store = await AddStoreAsync();
        await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);

        var result = await Handler(new OvertakenUserRepository(_users)).Handle(new AssignStoreMemberCommand(store.Id, "asha@example.com"), CancellationToken.None);

        // The membership was written before the role change was refused; undoing it is the unit of work's job (see the integration test).
        Assert.Same(StoreErrors.UserNotACustomer, result.Error);
    }

    [Fact]
    public async Task ADatabaseThatWasNeverSeeded_IsAnException_NotAnError()
    {
        var store = await AddStoreAsync();
        await AddUserAsync(_users, "asha@example.com", RoleIds.Customer);
        var handler = new AssignStoreMemberCommandHandler(_stores, _users, new FakeRoleRepository(seeded: false), _members);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new AssignStoreMemberCommand(store.Id, "asha@example.com"), CancellationToken.None));
    }
}
