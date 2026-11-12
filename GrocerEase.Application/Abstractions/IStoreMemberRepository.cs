using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>Where the memberships of stores are kept: which users run which store. Application owns the interface and Infrastructure implements it.</summary>
public interface IStoreMemberRepository
{
    /// <summary>
    /// Records that a user runs a store. Returns <c>false</c>, and records nothing, when the user is already a member of a store. A
    /// handler checks the user's role first, but two admins can assign the same customer at once, so the unique index on the user has
    /// the last word: this is an expected outcome, not an exception.
    /// </summary>
    Task<bool> TryAddAsync(StoreMember member, CancellationToken cancellationToken);
}
