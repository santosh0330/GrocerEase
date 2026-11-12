namespace GrocerEase.Domain.Entities;

/// <summary>
/// A shop that sells groceries. The entity guards its own invariants, like <see cref="User"/>: it cannot exist with an empty id,
/// a blank name or address, or text that would not fit its column. A store user belongs to one (see the store membership);
/// a store holds no list of them, because that is a different question with its own rules.
/// </summary>
public sealed class Store
{
    public const int RequiredIdVersion = 7;

    // The longest a name or an address may be. One definition: the validators reject longer input with a 400, the table's columns are
    // as wide, and the constructor refuses to build a store that would not fit.
    public const int MaxNameLength = 100;

    public const int MaxAddressLength = 300;

    public Store(Guid id, string name, string address)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        // Measured after trimming: padding is not stored, so it does not count. Two stores whose names differ only by spaces are one name.
        var trimmedName = name.Trim();
        var trimmedAddress = address.Trim();
        ThrowIfLongerThan(trimmedName, MaxNameLength, nameof(name));
        ThrowIfLongerThan(trimmedAddress, MaxAddressLength, nameof(address));

        Id = id;
        Name = trimmedName;
        Address = trimmedAddress;
    }

    private static void ThrowIfLongerThan(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value must be at most {maxLength} characters.", parameterName);
        }
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Address { get; }
}
