namespace GrocerEase.Application.Abstractions;

/// <summary>Turns a password into something safe to store, and checks a password against it. Infrastructure implements it.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password with a fresh random salt, so two users with the same password get different hashes.</summary>
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored hash. Pass <c>null</c> when there is no such user: the call still does the
    /// full amount of work and returns <c>false</c>, so the time it takes does not reveal whether an email is registered.
    /// </summary>
    bool Verify(string? passwordHash, string password);
}
