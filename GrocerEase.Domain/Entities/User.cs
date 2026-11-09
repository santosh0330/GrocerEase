namespace GrocerEase.Domain.Entities;

/// <summary>A person with an account. The entity guards its own invariants: it cannot exist with an empty id or blank text.</summary>
public sealed class User
{
    public User(Guid id, string firstName, string lastName, string email, string password)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Password = password;
    }

    public Guid Id { get; }

    public string FirstName { get; }

    public string LastName { get; }

    public string Email { get; }

    // TEMPORARY: stored and compared as plain text because no password hasher exists yet.
    // Replace with a hash (and an IPasswordHasher in Application) before any real storage; do not deploy as is.
    public string Password { get; }
}
