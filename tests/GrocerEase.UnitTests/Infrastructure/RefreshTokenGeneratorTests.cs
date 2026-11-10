using System.Security.Cryptography;
using System.Text;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;

namespace GrocerEase.UnitTests.Infrastructure;

public class RefreshTokenGeneratorTests
{
    private readonly RefreshTokenGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsDifferentTokensEveryTime()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(1000, tokens.Count);
    }

    [Fact]
    public void Generate_ReturnsThirtyTwoRandomBytes_InUrlSafeText()
    {
        var token = _generator.Generate();

        // 32 bytes in base64url, without padding, is 43 characters of A-Z a-z 0-9 - _ : safe in JSON, a URL or a header.
        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(32, System.Buffers.Text.Base64Url.DecodeFromChars(token).Length);
    }

    [Fact]
    public void Hash_IsTheSha256OfTheTokenText()
    {
        var hash = _generator.Hash("some-token");

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("some-token")), hash);
        Assert.Equal(RefreshToken.HashLength, hash.Length);
    }

    [Fact]
    public void Hash_IsTheSameForTheSameToken_AndDifferentForAnother()
    {
        Assert.Equal(_generator.Hash("a"), _generator.Hash("a"));
        Assert.NotEqual(_generator.Hash("a"), _generator.Hash("b"));
    }

    [Fact]
    public void TheHash_DoesNotContainTheToken()
    {
        var token = _generator.Generate();

        var hash = _generator.Hash(token);

        Assert.DoesNotContain(token, Convert.ToHexStringLower(hash), StringComparison.Ordinal);
        Assert.DoesNotContain(token, Convert.ToBase64String(hash), StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_RejectsNull() => Assert.Throws<ArgumentNullException>(() => _generator.Hash(null!));

    [Fact]
    public void ABuiltEntity_CanHoldAGeneratedHash() =>
        Assert.Equal(
            RefreshToken.HashLength,
            RefreshToken.Start(Guid.CreateVersion7(), _generator.Hash(_generator.Generate()), DateTimeOffset.UtcNow, TimeSpan.FromDays(3), TimeSpan.FromDays(30)).TokenHash.Length);
}
