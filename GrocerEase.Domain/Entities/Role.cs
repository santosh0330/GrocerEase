namespace GrocerEase.Domain.Entities;

/// <summary>
/// A named kind of user, such as <c>Customer</c> or <c>Admin</c>. Every user holds exactly one. Like <see cref="User"/>, it guards
/// its own invariants: it cannot exist with an empty id or a blank name.
/// </summary>
public sealed class Role
{
    public const int RequiredIdVersion = 7;

    // The longest a role name may be. One definition: the table's column is as wide, and the constructor below refuses a name
    // that would not fit.
    public const int MaxNameLength = 50;

    public Role(Guid id, string name)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            // The same rule as for a user: a version 7 id sorts by creation time, which keeps inserts at the end of the index.
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"The value must be at most {MaxNameLength} characters.", nameof(name));
        }

        Id = id;
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }
}
