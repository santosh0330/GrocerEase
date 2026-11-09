using GrocerEase.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// Hashes passwords with ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>: PBKDF2 with a random salt per hash,
/// and the algorithm and iteration count stored inside the hash string, so they can be raised later without breaking old hashes.
/// Nothing here is hand-rolled cryptography.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    // The Identity hasher is generic over a user type it never reads for hashing, so any reference type will do.
    private static readonly object NoUser = new();
    private static readonly PasswordHasher<object> Hasher = new();

    // A real hash of a password nobody knows. Verifying against it costs the same as verifying against a real user's hash.
    private static readonly string DummyHash = Hasher.HashPassword(NoUser, Guid.NewGuid().ToString("N"));

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        return Hasher.HashPassword(NoUser, password);
    }

    public bool Verify(string? passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var result = Hasher.VerifyHashedPassword(NoUser, passwordHash ?? DummyHash, password);

        // SuccessRehashNeeded means "right password, but the hash uses older settings". The password is still right.
        // Upgrading the stored hash on login is a later improvement.
        return passwordHash is not null && result != PasswordVerificationResult.Failed;
    }
}
