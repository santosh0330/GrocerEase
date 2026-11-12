using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Handlers;

/// <summary>
/// Makes a customer a store user by assigning them to a store: it records the membership and changes the role, and the two are one
/// thing. It is registered as a transactional handler, so if either write cannot be done, neither is kept.
/// </summary>
public sealed class AssignStoreMemberCommandHandler(
    IStoreRepository storeRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IStoreMemberRepository memberRepository) : ICommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>>
{
    public async Task<Result<StoreMemberResult>> Handle(AssignStoreMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The checks give a good error message. They are not what keeps the data right: another request can change the user between
        // here and the writes below, and the writes are written so that it cannot slip through.
        var store = await storeRepository.GetByIdAsync(command.StoreId, cancellationToken);
        if (store is null)
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.NotFound);
        }

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.UserNotFound);
        }

        // Both roles are seeded: a missing one is a broken database, which is an exception, not something the caller did wrong.
        var customer = await roleRepository.GetRequiredByNameAsync(RoleNames.Customer, cancellationToken);
        var storeUser = await roleRepository.GetRequiredByNameAsync(RoleNames.StoreUser, cancellationToken);
        if (user.RoleId != customer.Id)
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.UserNotACustomer);
        }

        // The unique index on the user decides whether they already run a store, and the role change only happens if they are still a
        // customer. A failure of either returns the same error, and the decorator undoes whatever was written before it.
        if (!await memberRepository.TryAddAsync(new StoreMember(Guid.CreateVersion7(), store.Id, user.Id), cancellationToken)
            || !await userRepository.TryChangeRoleAsync(user.Id, customer.Id, storeUser.Id, cancellationToken))
        {
            return Result.Failure<StoreMemberResult>(StoreErrors.UserNotACustomer);
        }

        return Result.Success(new StoreMemberResult(store.Id, user.Id, user.FirstName, user.LastName, user.Email));
    }
}
