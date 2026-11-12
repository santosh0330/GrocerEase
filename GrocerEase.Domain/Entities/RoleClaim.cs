namespace GrocerEase.Domain.Entities;

/// <summary>
/// One statement a <see cref="Role"/> makes about its holders, as a <c>(type, value)</c> pair. For now the only type is
/// <see cref="Permissions.ClaimType"/>: <c>("permission", "roles.read")</c> says the role may read roles. Like the other entities
/// it guards its own invariants: no empty ids, no blank or oversized text.
/// </summary>
public sealed class RoleClaim
{
    public const int RequiredIdVersion = 7;

    // The longest a type or a value may be. One definition: the table's columns are as wide, and the constructor refuses text
    // that would not fit.
    public const int MaxTypeLength = 50;

    public const int MaxValueLength = 100;

    public RoleClaim(Guid id, Guid roleId, string type, string value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(roleId, Guid.Empty); // a claim belongs to a role
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ThrowIfLongerThan(type, MaxTypeLength, nameof(type));
        ThrowIfLongerThan(value, MaxValueLength, nameof(value));

        Id = id;
        RoleId = roleId;
        Type = type;
        Value = value;
    }

    private static void ThrowIfLongerThan(string text, int maxLength, string parameterName)
    {
        if (text.Length > maxLength)
        {
            throw new ArgumentException($"The value must be at most {maxLength} characters.", parameterName);
        }
    }

    public Guid Id { get; }

    /// <summary>The id of the role this claim belongs to. The role holds the claim, not the other way round.</summary>
    public Guid RoleId { get; }

    public string Type { get; }

    public string Value { get; }
}
