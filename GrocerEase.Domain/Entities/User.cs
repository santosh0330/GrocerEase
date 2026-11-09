namespace GrocerEase.Domain.Entities;

/// <summary>A person with an account. The entity guards its own invariants: it cannot exist with an empty id or blank text.</summary>
public sealed class User
{
    public User(Guid id, string firstName, string lastName, string email, string passwordHash)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
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
