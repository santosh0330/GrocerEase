namespace GrocerEase.Domain.Entities;

/// <summary>A person with an account. The entity guards its own invariants: it cannot exist with an empty id or blank text.</summary>
public sealed class User
{
    public const int RequiredIdVersion = 7;

    // The longest a name or an email may be. One definition: the validators reject longer input with a 400, the table's
    // columns are as wide, and the constructor below refuses to build a user that would not fit.
    public const int MaxNameLength = 100;

    // The longest an email address can be, by RFC 5321 (254 characters).
    public const int MaxEmailLength = 254;

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
        ThrowIfLongerThan(firstName, MaxNameLength, nameof(firstName));
        ThrowIfLongerThan(lastName, MaxNameLength, nameof(lastName));
        var normalizedEmail = NormalizeEmail(email); // measured after trimming: padding is not stored, so it does not count
        ThrowIfLongerThan(normalizedEmail, MaxEmailLength, nameof(email));

        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = normalizedEmail;
        PasswordHash = passwordHash;
    }

    /// <summary>
    /// The one form an email is stored and looked up in: trimmed and lower case. <c>Ada@x.com</c> and <c>ada@x.com</c> are the same
    /// mailbox, so they must be the same account; with one stored form, a unique index on the column is enough to guarantee it.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Trim().ToLowerInvariant();
    }

    private static void ThrowIfLongerThan(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value must be at most {maxLength} characters.", parameterName);
        }
    }

    public Guid Id { get; }

    public string FirstName { get; }

    public string LastName { get; }

    public string Email { get; }

    /// <summary>The output of <c>IPasswordHasher.Hash</c>, never the password itself.</summary>
    public string PasswordHash { get; }
}
