using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;

namespace GrocerEase.Application.Stores.Common;

/// <summary>
/// The ownership rule: a store user may touch the things of the stores they run, and no others. A permission says <i>what kind</i> of
/// user may do something (<c>products.manage</c>); only the database can say <i>in which store</i>, so every store-owned use case asks
/// here first. It is asked of the database on every request, not read from the token: removing a store user must stop them at once,
/// while a token keeps saying what it said until it expires.
/// </summary>
/// <remarks>
/// A store the caller does not run answers <see cref="StoreErrors.NotFound"/>, the same as a store that does not exist. A 403 would tell
/// a store user that a store with that id exists, the leak that login avoids by answering one error for a wrong email and a wrong
/// password. Scoped, like the repository it reads through.
/// </remarks>
public sealed class StoreAccess(IStoreMemberRepository memberRepository)
{
    /// <summary>Returns <c>null</c> when the user runs the store, and the error to answer with when they do not.</summary>
    public async Task<ResultError?> RequireMemberAsync(Guid storeId, Guid userId, CancellationToken cancellationToken) =>
        await memberRepository.IsMemberAsync(storeId, userId, cancellationToken) ? null : StoreErrors.NotFound;
}
