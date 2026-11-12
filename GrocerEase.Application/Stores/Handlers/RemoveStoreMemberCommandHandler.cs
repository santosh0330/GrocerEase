using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Handlers;

/// <summary>
/// Takes a store user out of a store and makes them a customer again: it removes the membership and changes the role, and the two are
/// one thing, like assigning. It is registered as a transactional handler, so if either write cannot be done, neither is kept.
/// </summary>
public sealed class RemoveStoreMemberCommandHandler(
    IStoreRepository storeRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IStoreMemberRepository memberRepository) : ICommandHandler<RemoveStoreMemberCommand, Result<StoreMemberResult>>
{
    public async Task<Result<StoreMemberResult>> Handle(RemoveStoreMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await storeRepository.GetByIdAsync(command.StoreId, cancellationToken) is null)
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.NotFound);
        }

        // Read before the membership goes, because the answer says who was removed. A user who is not a member has nothing to read.
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);

        // The statement names the store as well as the user: a member of another store is not "a member of this one", and is untouched.
        if (user is null || !await memberRepository.TryRemoveAsync(command.StoreId, command.UserId, cancellationToken))
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.MemberNotFound);
        }

        var storeUser = await roleRepository.GetRequiredByNameAsync(RoleNames.StoreUser, cancellationToken);
        var customer = await roleRepository.GetRequiredByNameAsync(RoleNames.Customer, cancellationToken);
        if (!await userRepository.TryChangeRoleAsync(user.Id, storeUser.Id, customer.Id, cancellationToken))
        {
            // Not something the caller did: a member who does not hold the StoreUser role means the data is wrong (the two are only ever
            // written together). An exception is logged and answered with a generic 500, and the unit of work undoes the removal.
            throw new InvalidOperationException($"User {user.Id} was a member of store {command.StoreId} but does not hold the {RoleNames.StoreUser} role.");
        }

        return Result.Success(new StoreMemberResult(command.StoreId, user.Id, user.FirstName, user.LastName, user.Email));
    }
}
