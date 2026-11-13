using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where stores are kept. Application owns the interface and Infrastructure implements it, like <see cref="IUserRepository"/>.
/// </summary>
public interface IStoreRepository
{
    /// <summary>Finds a store by id. Returns <c>null</c> when there is none.</summary>
    Task<Store?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the stores, ordered by name. <paramref name="page"/> counts from 1. A page past the end is empty.</summary>
    Task<PagedResult<Store>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new store. Returns <c>false</c>, and stores nothing, when the name is already taken. The unique index has the last
    /// word, as for a user's email: two admins creating the same store at once cannot both win, and that is an expected outcome, not an
    /// exception.
    /// </summary>
    Task<bool> TryAddAsync(Store store, CancellationToken cancellationToken);
}
