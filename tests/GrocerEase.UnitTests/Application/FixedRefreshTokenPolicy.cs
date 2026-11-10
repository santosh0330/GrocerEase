using GrocerEase.Application.Abstractions;

namespace GrocerEase.UnitTests.Application;

internal sealed class FixedRefreshTokenPolicy(TimeSpan lifetime, TimeSpan maxLifetime) : IRefreshTokenPolicy
{
    public TimeSpan Lifetime { get; } = lifetime;

    public TimeSpan MaxLifetime { get; } = maxLifetime;
}
