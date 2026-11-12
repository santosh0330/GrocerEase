namespace GrocerEase.Domain.Entities;

/// <summary>
/// A named kind of user, such as <c>Customer</c> or <c>Admin</c>. Every user holds exactly one. A role grants what its
/// <see cref="Claims"/> say, and the permissions among them decide what an endpoint allows. Like <see cref="User"/>, it guards its
/// own invariants: it cannot exist with an empty id or a blank name.
/// </summary>
public sealed class Role
{
    public const int RequiredIdVersion = 7;

    // The longest a role name may be. One definition: the table's column is as wide, and the constructor below refuses a name
    // that would not fit.
    public const int MaxNameLength = 50;

    // Filled by the constructor's callers through AddClaim, and by EF Core when it reads a role, which is why it is a field the
    // mapping can reach and not a constructor parameter (EF cannot pass a navigation to a constructor).
    private readonly List<RoleClaim> _claims = [];

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

    /// <summary>Everything this role states about its holders. Read only: change it through <see cref="AddClaim"/>.</summary>
    public IReadOnlyList<RoleClaim> Claims => _claims.AsReadOnly(); // a wrapper, so a cast back to List cannot reach the field

    /// <summary>The names of the permissions this role grants, in a stable order, without duplicates.</summary>
    public IReadOnlyList<string> GrantedPermissions =>
    [
        .. _claims
            .Where(claim => string.Equals(claim.Type, Permissions.ClaimType, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .Order(StringComparer.Ordinal),
    ];

    public bool Grants(string permission) =>
        _claims.Exists(claim =>
            string.Equals(claim.Type, Permissions.ClaimType, StringComparison.Ordinal)
            && string.Equals(claim.Value, permission, StringComparison.Ordinal));

    /// <summary>Adds a claim to this role. The same type and value twice is a mistake, so it is refused.</summary>
    public RoleClaim AddClaim(string type, string value)
    {
        var claim = new RoleClaim(Guid.CreateVersion7(), Id, type, value);
        if (_claims.Exists(existing =>
                string.Equals(existing.Type, claim.Type, StringComparison.Ordinal)
                && string.Equals(existing.Value, claim.Value, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The role '{Name}' already has the claim '{type}' = '{value}'.", nameof(value));
        }

        _claims.Add(claim);
        return claim;
    }

    /// <summary>Adds a permission claim: shorthand for <c>AddClaim(Permissions.ClaimType, permission)</c>.</summary>
    public RoleClaim Grant(string permission) => AddClaim(Permissions.ClaimType, permission);
}
