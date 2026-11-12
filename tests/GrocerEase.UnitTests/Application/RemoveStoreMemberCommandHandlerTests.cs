using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Handlers;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class RemoveStoreMemberCommandHandlerTests
{
    private readonly FakeStoreRepository _stores = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeStoreMemberRepository _members = new();

    private RemoveStoreMemberCommandHandler Handler() => new(_stores, _users, new FakeRoleRepository(), _members);

    private async Task<Store> AddStoreAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        Assert.True(await _stores.TryAddAsync(store, CancellationToken.None));
        return store;
    }

    private async Task<User> AddUserAsync(Guid roleId)
    {
        var user = new User(Guid.CreateVersion7(), "Asha", "Rai", $"asha-{Guid.NewGuid():N}@example.com", "a-password-hash", roleId);
        Assert.True(await _users.TryAddAsync(user, CancellationToken.None));
        return user;
    }

    private async Task<(Store Store, User User)> AStoreUserAsync()
    {
        var store = await AddStoreAsync();
        var user = await AddUserAsync(RoleIds.StoreUser);
        Assert.True(await _members.TryAddAsync(new StoreMember(Guid.CreateVersion7(), store.Id, user.Id), CancellationToken.None));
        return (store, user);
    }

    [Fact]
    public async Task AStoreUser_IsTakenOutOfTheStore_AndBecomesACustomerAgain()
    {
        var (store, user) = await AStoreUserAsync();

        var result = await Handler().Handle(new RemoveStoreMemberCommand(store.Id, user.Id), CancellationToken.None);

        Assert.Equal(new StoreMemberResult(store.Id, user.Id, "Asha", "Rai", user.Email), result.Value);
        Assert.Empty(_members.Members);
        Assert.Equal(RoleIds.Customer, (await _users.GetByIdAsync(user.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task AnUnknownStore_IsNotFound()
    {
        var (_, user) = await AStoreUserAsync();

        var result = await Handler().Handle(new RemoveStoreMemberCommand(Guid.CreateVersion7(), user.Id), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Single(_members.Members);
    }

    [Fact]
    public async Task AnUnknownUser_IsMemberNotFound()
    {
        var store = await AddStoreAsync();

        var result = await Handler().Handle(new RemoveStoreMemberCommand(store.Id, Guid.CreateVersion7()), CancellationToken.None);

        Assert.Same(StoreErrors.MemberNotFound, result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task ACustomerWhoRunsNoStore_IsMemberNotFound_AndStaysACustomer()
    {
        var store = await AddStoreAsync();
        var customer = await AddUserAsync(RoleIds.Customer);

        var result = await Handler().Handle(new RemoveStoreMemberCommand(store.Id, customer.Id), CancellationToken.None);

        Assert.Same(StoreErrors.MemberNotFound, result.Error);
        Assert.Equal(RoleIds.Customer, (await _users.GetByIdAsync(customer.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task AMemberOfAnotherStore_IsNotMemberOfThisOne_AndIsUntouched()
    {
        var (_, user) = await AStoreUserAsync();
        var otherStore = await AddStoreAsync();

        var result = await Handler().Handle(new RemoveStoreMemberCommand(otherStore.Id, user.Id), CancellationToken.None);

        Assert.Same(StoreErrors.MemberNotFound, result.Error);
        Assert.Single(_members.Members);
        Assert.Equal(RoleIds.StoreUser, (await _users.GetByIdAsync(user.Id, CancellationToken.None))?.RoleId);
    }

    [Fact]
    public async Task AMemberWhoDoesNotHoldTheStoreUserRole_IsBrokenData_AnExceptionAndNotAnError()
    {
        var store = await AddStoreAsync();
        var admin = await AddUserAsync(RoleIds.Admin); // a membership the application never writes: someone changed the role by hand
        await _members.TryAddAsync(new StoreMember(Guid.CreateVersion7(), store.Id, admin.Id), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler().Handle(new RemoveStoreMemberCommand(store.Id, admin.Id), CancellationToken.None));
    }
}
