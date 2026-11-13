namespace GrocerEase.Application.Common;

/// <summary>The one set of paging limits every list uses, so a client learns them once.</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;

    /// <summary>The most items one request may ask for. A list that grows with the data needs a ceiling, or it is a slow endpoint nobody has found yet.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The highest page number. Offset paging reads and throws away every row before the page, so a page deep into a big list is a scan,
    /// not browsing; the cap also keeps <c>(page - 1) * pageSize</c> inside an <c>int</c>.
    /// </summary>
    public const int MaxPage = 100_000;
}
