using GrocerEase.Infrastructure.Authentication;

namespace GrocerEase.UnitTests.Infrastructure;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainThePassword()
    {
        var hash = _hasher.Hash("s3cret!-password");

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain("s3cret!-password", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_GivesADifferentResultEachTime_BecauseOfTheSalt() =>
        Assert.NotEqual(_hasher.Hash("s3cret!-password"), _hasher.Hash("s3cret!-password"), StringComparer.Ordinal);

    [Fact]
    public void Verify_AcceptsTheRightPassword()
    {
        var hash = _hasher.Hash("s3cret!-password");

        Assert.True(_hasher.Verify(hash, "s3cret!-password"));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("S3CRET!-PASSWORD")]
    [InlineData("")]
    public void Verify_RejectsAWrongPassword(string attempt)
    {
        var hash = _hasher.Hash("s3cret!-password");

        Assert.False(_hasher.Verify(hash, attempt));
    }

    [Fact]
    public void Verify_WithNoHash_ReturnsFalseEvenForAnyPassword() =>
        Assert.False(_hasher.Verify(null, "s3cret!-password"));

    [Fact]
    public void Verify_WithNoHash_DoesTheWorkOfARealCheck()
    {
        // The point of the dummy hash: an unknown email must not answer noticeably faster than a wrong password.
        // Timing is flaky to assert tightly, so only check the unknown case is not near-instant (a skipped hash is microseconds).
        var hash = _hasher.Hash("s3cret!-password");
        _ = _hasher.Verify(hash, "warm-up");

        var real = System.Diagnostics.Stopwatch.StartNew();
        _ = _hasher.Verify(hash, "wrong");
        real.Stop();
        var unknown = System.Diagnostics.Stopwatch.StartNew();
        _ = _hasher.Verify(null, "wrong");
        unknown.Stop();

        Assert.True(unknown.Elapsed > real.Elapsed / 4, $"unknown {unknown.Elapsed} vs real {real.Elapsed}");
    }

    [Fact]
    public void Verify_RejectsAMalformedHash() =>
        Assert.Throws<FormatException>(() => _hasher.Verify("not-a-hash", "s3cret!-password"));

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(null!));
        Assert.Throws<ArgumentNullException>(() => _hasher.Verify("x", null!));
    }
}
