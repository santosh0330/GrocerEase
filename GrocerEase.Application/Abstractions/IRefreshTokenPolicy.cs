namespace GrocerEase.Application.Abstractions;

/// <summary>How long refresh tokens live. Configuration in Infrastructure supplies it, so Application holds no settings.</summary>
public interface IRefreshTokenPolicy
{
    /// <summary>How long one refresh token is good for. Each refresh starts a new window of this length.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>The most a session may last from sign-in, however often it is refreshed. Never more than 30 days.</summary>
    TimeSpan MaxLifetime { get; }
}
