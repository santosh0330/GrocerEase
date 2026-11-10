using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class RefreshCommandValidatorTests
{
    private readonly RefreshCommandValidator _validator = new();

    [Fact]
    public void ARealLookingToken_IsValid() =>
        Assert.True(_validator.Validate(new RefreshCommand(new string('a', 43))).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyOrBlankToken_IsRejected_WithOneMessage(string token)
    {
        var result = _validator.Validate(new RefreshCommand(token));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(RefreshCommand.RefreshToken), error.PropertyName);
    }

    [Fact]
    public void ATokenOfExactlyTheLimit_IsValid_AndOneMoreIsNot()
    {
        Assert.True(_validator.Validate(new RefreshCommand(new string('a', RefreshCommandValidator.MaxRefreshTokenLength))).IsValid);
        Assert.False(_validator.Validate(new RefreshCommand(new string('a', RefreshCommandValidator.MaxRefreshTokenLength + 1))).IsValid);
    }

    [Fact]
    public void TheMessage_NeverEchoesTheTokenBack()
    {
        var token = new string('x', RefreshCommandValidator.MaxRefreshTokenLength + 1);

        var result = _validator.Validate(new RefreshCommand(token));

        Assert.DoesNotContain(result.Errors, error => error.ErrorMessage.Contains(token, StringComparison.Ordinal));
    }
}
