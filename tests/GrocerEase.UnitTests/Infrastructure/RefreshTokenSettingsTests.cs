using System.ComponentModel.DataAnnotations;
using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure.Authentication;

namespace GrocerEase.UnitTests.Infrastructure;

public class RefreshTokenSettingsTests
{
    private static List<ValidationResult> Validate(RefreshTokenSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ThreeDaysWithAThirtyDayLimit_IsAccepted() =>
        Assert.Empty(Validate(new RefreshTokenSettings { LifetimeDays = 3, MaxLifetimeDays = 30 }));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    public void TheEdgesAreAccepted(int lifetime, int maxLifetime) =>
        Assert.Empty(Validate(new RefreshTokenSettings { LifetimeDays = lifetime, MaxLifetimeDays = maxLifetime }));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void ALifetimeOutsideOneToThirtyDays_IsRejected_AndTheMessageNamesTheSetting(int days)
    {
        var error = Assert.Single(Validate(new RefreshTokenSettings { LifetimeDays = days, MaxLifetimeDays = 30 }));

        Assert.Contains("RefreshToken:LifetimeDays", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void AMaximumOutsideOneToThirtyDays_IsRejected_AndTheMessageNamesTheSetting(int days)
    {
        var error = Assert.Single(Validate(new RefreshTokenSettings { LifetimeDays = 1, MaxLifetimeDays = days }));

        Assert.Contains("RefreshToken:MaxLifetimeDays", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ALifetimeLongerThanTheMaximum_IsRejected()
    {
        var error = Assert.Single(Validate(new RefreshTokenSettings { LifetimeDays = 10, MaxLifetimeDays = 5 }));

        Assert.Contains("must not be more than", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionThatWasNotConfigured_IsRejected() =>
        Assert.NotEmpty(Validate(new RefreshTokenSettings()));

    [Fact]
    public void AsAPolicy_ItGivesTheLifetimesAsTimeSpans()
    {
        IRefreshTokenPolicy policy = new RefreshTokenSettings { LifetimeDays = 3, MaxLifetimeDays = 30 };

        Assert.Equal(TimeSpan.FromDays(3), policy.Lifetime);
        Assert.Equal(TimeSpan.FromDays(30), policy.MaxLifetime);
    }
}
