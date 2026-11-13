using GrocerEase.Application.Common;

namespace GrocerEase.UnitTests.Application;

public class PagedResultTests
{
    [Fact]
    public void Map_ChangesTheItems_AndKeepsThePaging()
    {
        var page = new PagedResult<int>([1, 2, 3], Page: 2, PageSize: 3, TotalCount: 10);

        var mapped = page.Map(number => $"#{number}");

        Assert.Equal(["#1", "#2", "#3"], mapped.Items, StringComparer.Ordinal);
        Assert.Equal((2, 3, 10), (mapped.Page, mapped.PageSize, mapped.TotalCount));
    }

    [Fact]
    public void TheLimits_AreOneSetForEveryList() =>
        Assert.Equal((20, 100), (Paging.DefaultPageSize, Paging.MaxPageSize));
}
