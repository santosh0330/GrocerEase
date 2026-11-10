using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ThreeDays = TimeSpan.FromDays(3);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    private static byte[] Hash(byte fill = 1) => Enumerable.Repeat(fill, RefreshToken.HashLength).ToArray();

    private static RefreshToken Started(byte fill = 1) => RefreshToken.Start(Guid.CreateVersion7(), Hash(fill), Now, ThreeDays, ThirtyDays);

    private static RefreshToken Build(
        Guid? id = null, Guid? userId = null, Guid? familyId = null, byte[]? hash = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null, DateTimeOffset? familyExpiresAt = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            userId ?? Guid.CreateVersion7(),
            familyId ?? Guid.CreateVersion7(),
            hash ?? Hash(),
            createdAt ?? Now,
            expiresAt ?? Now + ThreeDays,
            familyExpiresAt ?? Now + ThirtyDays);

    [Fact]
    public void TheLimitIsThirtyDays() => Assert.Equal(TimeSpan.FromDays(30), RefreshToken.MaxLifetime);

    [Fact]
    public void Start_BeginsANewFamily_ThatExpiresAfterTheThreeDayLifetime_AndEndsAfterTheMaximum()
    {
        var userId = Guid.CreateVersion7();

        var token = RefreshToken.Start(userId, Hash(), Now, ThreeDays, ThirtyDays);

        Assert.Equal(userId, token.UserId);
        Assert.Equal(Now, token.CreatedAt);
        Assert.Equal(Now + ThreeDays, token.ExpiresAt);
        Assert.Equal(Now + ThirtyDays, token.FamilyExpiresAt);
        Assert.NotEqual(token.Id, token.FamilyId);
        Assert.Equal(7, token.Id.Version);
        Assert.Equal(7, token.FamilyId.Version);
    }

    [Fact]
    public void Start_GivesEachSignInItsOwnFamily() =>
        Assert.NotEqual(Started().FamilyId, Started().FamilyId);

    [Fact]
    public void Start_WithALifetimeLongerThanTheFamily_ExpiresWhenTheFamilyDoes()
    {
        var token = RefreshToken.Start(Guid.CreateVersion7(), Hash(), Now, TimeSpan.FromDays(10), TimeSpan.FromDays(5));

        Assert.Equal(Now + TimeSpan.FromDays(5), token.ExpiresAt);
    }

    [Fact]
    public void Start_AcceptsExactlyThirtyDays() =>
        Assert.Equal(Now + ThirtyDays, RefreshToken.Start(Guid.CreateVersion7(), Hash(), Now, ThirtyDays, ThirtyDays).ExpiresAt);

    [Fact]
    public void Start_RefusesAFamilyLongerThanThirtyDays() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RefreshToken.Start(Guid.CreateVersion7(), Hash(), Now, ThreeDays, ThirtyDays + TimeSpan.FromSeconds(1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Start_RefusesADurationThatIsNotPositive(int days)
    {
        var duration = TimeSpan.FromDays(days);

        Assert.Throws<ArgumentOutOfRangeException>(() => RefreshToken.Start(Guid.CreateVersion7(), Hash(), Now, duration, ThirtyDays));
        Assert.Throws<ArgumentOutOfRangeException>(() => RefreshToken.Start(Guid.CreateVersion7(), Hash(), Now, ThreeDays, duration));
    }

    [Fact]
    public void ANewToken_IsNeitherConsumedNorRevokedNorExpired()
    {
        var token = Started();

        Assert.Null(token.ConsumedAt);
        Assert.Null(token.RevokedAt);
        Assert.False(token.IsSpent);
        Assert.False(token.IsExpired(Now));
    }

    [Fact]
    public void IsExpired_IsTrueFromTheExpiryInstantOn()
    {
        var token = Started();

        Assert.False(token.IsExpired(token.ExpiresAt - TimeSpan.FromTicks(1)));
        Assert.True(token.IsExpired(token.ExpiresAt));
        Assert.True(token.IsExpired(token.ExpiresAt + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void Successor_StaysInTheSameFamily_ForTheSameUser_WithTheSameHardEnd()
    {
        var first = Started();
        var later = Now + TimeSpan.FromDays(2);

        var next = first.Successor(Hash(2), later, ThreeDays);

        Assert.Equal(first.UserId, next.UserId);
        Assert.Equal(first.FamilyId, next.FamilyId);
        Assert.Equal(first.FamilyExpiresAt, next.FamilyExpiresAt);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(later, next.CreatedAt);
    }

    [Fact]
    public void Successor_GetsAFreshThreeDayWindow()
    {
        var later = Now + TimeSpan.FromDays(2);

        var next = Started().Successor(Hash(2), later, ThreeDays);

        Assert.Equal(later + ThreeDays, next.ExpiresAt);
    }

    [Fact]
    public void Successor_NeverOutlivesTheFamily()
    {
        var first = Started();
        var nearTheEnd = Now + TimeSpan.FromDays(29);

        var next = first.Successor(Hash(2), nearTheEnd, ThreeDays);

        Assert.Equal(first.FamilyExpiresAt, next.ExpiresAt); // day 30, not day 32
    }

    [Fact]
    public void Successor_AtTheEndOfTheFamily_IsRefused()
    {
        var first = Started();

        // Nothing is left of the family: a token created at its end would expire at the moment it was made.
        Assert.Throws<ArgumentException>(() => first.Successor(Hash(2), first.FamilyExpiresAt, ThreeDays));
    }

    [Fact]
    public void TheHash_IsACopy_SoChangingTheCallersArrayChangesNothing()
    {
        var hash = Hash(7);

        var token = Build(hash: hash);
        hash[0] = 0;

        Assert.Equal(Hash(7), token.TokenHash);
    }

    [Fact]
    public void ARefreshTokenSeesItsValues()
    {
        var id = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var familyId = Guid.CreateVersion7();

        var token = Build(id, userId, familyId, Hash(9));

        Assert.Equal((id, userId, familyId), (token.Id, token.UserId, token.FamilyId));
        Assert.Equal(Hash(9), token.TokenHash);
        Assert.Equal((Now, Now + ThreeDays, Now + ThirtyDays), (token.CreatedAt, token.ExpiresAt, token.FamilyExpiresAt));
    }

    [Fact]
    public void ThrowsWhenTheIdIsEmpty() => Assert.Throws<ArgumentOutOfRangeException>(() => Build(id: Guid.Empty));

    [Fact]
    public void ThrowsWhenTheIdIsNotVersion7() => Assert.Throws<ArgumentException>(() => Build(id: Guid.NewGuid()));

    [Fact]
    public void ThrowsWhenTheUserIdIsEmpty() => Assert.Throws<ArgumentOutOfRangeException>(() => Build(userId: Guid.Empty));

    [Fact]
    public void ThrowsWhenTheFamilyIdIsEmpty() => Assert.Throws<ArgumentOutOfRangeException>(() => Build(familyId: Guid.Empty));

    [Fact]
    public void ThrowsWhenTheHashIsNull() =>
        Assert.Throws<ArgumentNullException>(() => new RefreshToken(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null!, Now, Now + ThreeDays, Now + ThirtyDays));

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void ThrowsWhenTheHashIsNotThirtyTwoBytes(int length) =>
        Assert.Throws<ArgumentException>(() => Build(hash: new byte[length]));

    [Fact]
    public void ThrowsWhenItExpiresAtOrBeforeItIsCreated()
    {
        Assert.Throws<ArgumentException>(() => Build(expiresAt: Now));
        Assert.Throws<ArgumentException>(() => Build(expiresAt: Now - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AcceptsALifetimeOfExactlyThirtyDays() =>
        Assert.Equal(Now + ThirtyDays, Build(expiresAt: Now + ThirtyDays).ExpiresAt);

    [Fact]
    public void ThrowsWhenItLivesLongerThanThirtyDays() =>
        Assert.Throws<ArgumentException>(() => Build(expiresAt: Now + ThirtyDays + TimeSpan.FromSeconds(1), familyExpiresAt: Now + TimeSpan.FromDays(40)));

    [Fact]
    public void ThrowsWhenItOutlivesItsFamily() =>
        Assert.Throws<ArgumentException>(() => Build(expiresAt: Now + ThreeDays, familyExpiresAt: Now + TimeSpan.FromDays(2)));
}
