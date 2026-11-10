using System.Security.Cryptography;
using System.Text;
using GrocerEase.Application.Abstractions;

namespace GrocerEase.UnitTests.Application;

/// <summary>Counts up instead of being random, so a test can tell the tokens apart and say which one it means. The hash is the real SHA-256.</summary>
internal sealed class FakeRefreshTokenGenerator : IRefreshTokenGenerator
{
    private int _count;

    public string Generate() => $"refresh-token-{Interlocked.Increment(ref _count)}";

    public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
