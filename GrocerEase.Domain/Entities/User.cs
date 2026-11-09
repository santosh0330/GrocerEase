namespace GrocerEase.Domain.Entities;

/// <summary>A person with an account. The entity guards its own invariants: it cannot exist with an empty id or blank text.</summary>
public sealed class User
{
    public const int RequiredIdVersion = 7;

    public User(Guid id, string firstName, string lastName, string email, string passwordHash)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            // A version 7 UUID starts with a millisecond timestamp, so new ids sort after older ones. In a database index
            // that keeps inserts at the end of the index, where a random (version 4) id scatters them across it.
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PasswordHash = passwordHash;
    }

    public Guid Id { get; }

    public string FirstName { get; }

    public string LastName { get; }

    public string Email { get; }

    /// <summary>The output of <c>IPasswordHasher.Hash</c>, never the password itself.</summary>
    public string PasswordHash { get; }
}
