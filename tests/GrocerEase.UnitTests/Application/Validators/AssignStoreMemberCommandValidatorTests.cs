using FluentValidation.TestHelper;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class AssignStoreMemberCommandValidatorTests
{
    private readonly AssignStoreMemberCommandValidator _validator = new();

    private static AssignStoreMemberCommand Valid => new(Guid.CreateVersion7(), "asha@example.com");

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void StoreId_Empty_IsInvalid() =>
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Email_BlankOrMalformed_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Email = value }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Email_TooLong_IsInvalid() =>
        _validator.TestValidate(Valid with { Email = new string('a', User.MaxEmailLength) + "@example.com" }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Email_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Email = "" }).ShouldHaveValidationErrorFor(x => x.Email));
}
