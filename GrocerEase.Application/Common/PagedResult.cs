namespace GrocerEase.Application.Common;

/// <summary>
/// One page of a list, and how big the whole list is. <paramref name="Page"/> counts from 1. A page past the end is not an error: it
/// has no items, and <paramref name="TotalCount"/> says how far the list really goes.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Maps every item and keeps the paging, so a list of entities becomes a list of results without losing its page.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new PagedResult<TOut>([.. Items.Select(map)], Page, PageSize, TotalCount);
    }
}
