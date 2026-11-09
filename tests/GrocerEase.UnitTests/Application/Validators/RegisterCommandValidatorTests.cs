using FluentValidation.TestHelper;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand Valid => new("Ada", "Lovelace", "ada@example.com", "Sup3r-s3cret!");

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FirstName_Blank_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { FirstName = value }).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact]
    public void FirstName_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { FirstName = new string('a', RegisterCommandValidator.MaxNameLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.FirstName);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LastName_Blank_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { LastName = value }).ShouldHaveValidationErrorFor(x => x.LastName);

    [Fact]
    public void LastName_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { LastName = new string('a', RegisterCommandValidator.MaxNameLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.LastName);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    public void Email_BlankOrMalformed_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Email = value }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Email_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { Email = new string('a', RegisterCommandValidator.MaxEmailLength) + "@example.com" })
            .ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Password_Empty_IsInvalid() =>
        _validator.TestValidate(Valid with { Password = "" }).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Password_TooShort_IsInvalid() =>
        _validator.TestValidate(Valid with { Password = new string('a', RegisterCommandValidator.MinPasswordLength - 1) })
            .ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Password_AtMinimumLength_IsValid() =>
        _validator.TestValidate(Valid with { Password = new string('a', RegisterCommandValidator.MinPasswordLength) })
            .ShouldNotHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Password_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { Password = new string('a', RegisterCommandValidator.MaxPasswordLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Password);

    // A rule stops at its first failure, so an empty value is reported once ("must not be empty"),
    // not again by every later check in the chain ("not a valid email", "too short").
    [Fact]
    public void Email_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Email = "" }).ShouldHaveValidationErrorFor(x => x.Email));

    [Fact]
    public void Password_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Password = "" }).ShouldHaveValidationErrorFor(x => x.Password));

    [Fact]
    public void Email_Malformed_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Email = "not-an-email" }).ShouldHaveValidationErrorFor(x => x.Email));

    [Fact]
    public void EveryEmptyField_IsStillReported_OneMessageEach()
    {
        var result = _validator.TestValidate(new RegisterCommand("", "", "", ""));

        Assert.Single(result.ShouldHaveValidationErrorFor(x => x.FirstName));
        Assert.Single(result.ShouldHaveValidationErrorFor(x => x.LastName));
        Assert.Single(result.ShouldHaveValidationErrorFor(x => x.Email));
        Assert.Single(result.ShouldHaveValidationErrorFor(x => x.Password));
    }
}
