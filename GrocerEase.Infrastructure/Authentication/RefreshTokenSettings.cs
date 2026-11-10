using System.ComponentModel.DataAnnotations;
using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// The <c>RefreshToken</c> section of the configuration, validated when the app starts: a value above the 30-day limit stops
/// the app at once, instead of issuing tokens the entity would then refuse to build.
/// </summary>
public sealed class RefreshTokenSettings : IRefreshTokenPolicy, IValidatableObject
{
    public const string SectionName = "RefreshToken";

    [Range(1, RefreshToken.MaxLifetimeDays, ErrorMessage = "RefreshToken:LifetimeDays must be between 1 and 30.")]
    public int LifetimeDays { get; init; }

    [Range(1, RefreshToken.MaxLifetimeDays, ErrorMessage = "RefreshToken:MaxLifetimeDays must be between 1 and 30.")]
    public int MaxLifetimeDays { get; init; }

    TimeSpan IRefreshTokenPolicy.Lifetime => TimeSpan.FromDays(LifetimeDays);

    TimeSpan IRefreshTokenPolicy.MaxLifetime => TimeSpan.FromDays(MaxLifetimeDays);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LifetimeDays > MaxLifetimeDays)
        {
            yield return new ValidationResult(
                "RefreshToken:LifetimeDays must not be more than RefreshToken:MaxLifetimeDays.",
                [nameof(LifetimeDays), nameof(MaxLifetimeDays)]);
        }
    }
}
