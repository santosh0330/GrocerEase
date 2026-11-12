using FluentValidation.TestHelper;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class CreateStoreCommandValidatorTests
{
    private readonly CreateStoreCommandValidator _validator = new();

    private static CreateStoreCommand Valid => new("Fresh Mart", "1 High Street");

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Blank_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Name = value }).ShouldHaveValidationErrorFor(x => x.Name);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Address_Blank_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Address = value }).ShouldHaveValidationErrorFor(x => x.Address);

    [Fact]
    public void Name_AtTheMaximum_IsValid_AndOneMoreIsNot()
    {
        _validator.TestValidate(Valid with { Name = new string('a', Store.MaxNameLength) }).ShouldNotHaveValidationErrorFor(x => x.Name);
        _validator.TestValidate(Valid with { Name = new string('a', Store.MaxNameLength + 1) }).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Address_AtTheMaximum_IsValid_AndOneMoreIsNot()
    {
        _validator.TestValidate(Valid with { Address = new string('a', Store.MaxAddressLength) }).ShouldNotHaveValidationErrorFor(x => x.Address);
        _validator.TestValidate(Valid with { Address = new string('a', Store.MaxAddressLength + 1) }).ShouldHaveValidationErrorFor(x => x.Address);
    }

    [Fact]
    public void Name_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name));

    [Fact]
    public void EveryBadFieldIsReported_NotOnlyTheFirst()
    {
        var result = _validator.TestValidate(new CreateStoreCommand("", ""));

        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.Address);
    }
}
