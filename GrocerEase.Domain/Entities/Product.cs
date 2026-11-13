namespace GrocerEase.Domain.Entities;

/// <summary>
/// Something a store sells. It belongs to exactly one store, and a store user manages the products of their own store only (that is a
/// membership check, made by the use case, not something the product knows). Like the other entities it guards its own invariants:
/// a version 7 id, a store, text that fits its column, and a price that is a real amount of money.
/// </summary>
/// <remarks>
/// A product is never deleted: it is <b>archived</b>. A past order keeps pointing at what was bought, and an archived product frees
/// its name for a new one. The entity is immutable, as <see cref="User"/> is; <see cref="WithDetails"/> returns an edited copy.
/// </remarks>
public sealed class Product
{
    public const int RequiredIdVersion = 7;

    // One definition for the validators (a 400), the table's columns and the constructor.
    public const int MaxNameLength = 100;

    public const int MaxDescriptionLength = 1000;

    /// <summary>Digits after the decimal point: money has cents, and the column is <c>numeric(12, 2)</c>.</summary>
    public const int PriceScale = 2;

    /// <summary>The most a <c>numeric(12, 2)</c> can hold: ten digits before the point.</summary>
    public const decimal MaxPrice = 9_999_999_999.99m;

    public Product(Guid id, Guid storeId, string name, string description, decimal price, DateTimeOffset? archivedAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty); // a product belongs to a store
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description); // may be empty: a product need not be described

        // Measured after trimming: padding is not stored, so it does not count.
        var trimmedName = name.Trim();
        var trimmedDescription = description.Trim();
        ThrowIfLongerThan(trimmedName, MaxNameLength, nameof(name));
        ThrowIfLongerThan(trimmedDescription, MaxDescriptionLength, nameof(description));
        ThrowIfNotAPrice(price);

        Id = id;
        StoreId = storeId;
        Name = trimmedName;
        Description = trimmedDescription;
        Price = price;
        ArchivedAt = archivedAt;
    }

    private static void ThrowIfLongerThan(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value must be at most {maxLength} characters.", parameterName);
        }
    }

    private static void ThrowIfNotAPrice(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(price, MaxPrice);
        if (decimal.Round(price, PriceScale) != price)
        {
            // Half a cent is not an amount anyone can pay, and rounding it silently would store a price the store did not choose.
            throw new ArgumentException($"The price must have at most {PriceScale} decimal places.", nameof(price));
        }
    }

    public Guid Id { get; }

    public Guid StoreId { get; }

    public string Name { get; }

    public string Description { get; }

    public decimal Price { get; }

    /// <summary>When the product was archived, or <c>null</c> while it is on sale.</summary>
    public DateTimeOffset? ArchivedAt { get; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>The same product with a new name, description and price: the id, the store and the archived state stay.</summary>
    public Product WithDetails(string name, string description, decimal price) =>
        new(Id, StoreId, name, description, price, ArchivedAt);
}
