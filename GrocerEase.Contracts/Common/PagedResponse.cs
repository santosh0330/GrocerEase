namespace GrocerEase.Contracts.Common;

/// <summary>One page of a list, as the paged routes return it. <c>page</c> counts from 1; a page past the end has no items.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
