using FluentValidation.TestHelper;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    private static LoginCommand Valid => new("ada@example.com", "whatever");

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Email_BlankOrMalformed_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Email = value }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Password_Empty_IsInvalid() =>
        _validator.TestValidate(Valid with { Password = "" }).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Password_Short_IsStillValid() =>
        _validator.TestValidate(Valid with { Password = "a" }).ShouldNotHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Password_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { Password = new string('a', RegisterCommandValidator.MaxPasswordLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Email_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Email = "" }).ShouldHaveValidationErrorFor(x => x.Email));

    [Fact]
    public void Email_Malformed_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Email = "not-an-email" }).ShouldHaveValidationErrorFor(x => x.Email));
}
