namespace GrocerEase.Application.Products.Common;

/// <summary>What happened when a product's details were written.</summary>
public enum ProductUpdateOutcome
{
    /// <summary>The product was on sale in that store, and now has the new details.</summary>
    Updated,

    /// <summary>There was nothing to change: no such product in that store, or it was archived (possibly a moment ago).</summary>
    NotFound,

    /// <summary>The store already sells another product with that name.</summary>
    DuplicateName,

    /// <summary>The product is on sale in that store, but it is no longer at the version the change was made from. Nothing was written.</summary>
    Outdated,
}
