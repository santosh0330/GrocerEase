using GrocerEase.Application.Abstractions;

namespace GrocerEase.UnitTests.Application;

/// <summary>A readable stand-in for the real hasher: the "hash" of <c>x</c> is <c>hashed:x</c>. Records every Verify call.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public List<string?> VerifiedHashes { get; } = [];

    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string? passwordHash, string password)
    {
        VerifiedHashes.Add(passwordHash);
        return passwordHash is not null && string.Equals(passwordHash, Hash(password), StringComparison.Ordinal);
    }
}
