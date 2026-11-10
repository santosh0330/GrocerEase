using GrocerEase.Application.Authentication.Common;

namespace GrocerEase.UnitTests.Application;

/// <summary>The fakes every refresh-token handler test needs, wired together. The policy is the real one: 3 days, at most 30.</summary>
internal sealed class RefreshTestKit
{
    public static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public FakeRefreshTokenRepository Tokens { get; } = new();

    public FakeRefreshTokenGenerator Generator { get; } = new();

    public FixedRefreshTokenPolicy Policy { get; } = new(TimeSpan.FromDays(3), TimeSpan.FromDays(30));

    public AdjustableTimeProvider Clock { get; } = new(Start);

    public RefreshTokenIssuer Issuer => new(Tokens, Generator, Policy, Clock);
}
