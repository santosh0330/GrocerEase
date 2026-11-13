using FluentValidation.TestHelper;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    private static CreateProductCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Milk", "Fresh milk", 2.50m);

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyIds_AreInvalid()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { ActorId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ActorId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Blank_IsInvalid(string value) =>
        _validator.TestValidate(Valid with { Name = value }).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void Name_AtTheMaximum_IsValid_AndOneMoreIsNot()
    {
        _validator.TestValidate(Valid with { Name = new string('a', Product.MaxNameLength) }).ShouldNotHaveValidationErrorFor(x => x.Name);
        _validator.TestValidate(Valid with { Name = new string('a', Product.MaxNameLength + 1) }).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Description_MayBeEmpty_ButNotNull_AndHasAMaximum()
    {
        _validator.TestValidate(Valid with { Description = "" }).ShouldNotHaveValidationErrorFor(x => x.Description);
        _validator.TestValidate(Valid with { Description = null! }).ShouldHaveValidationErrorFor(x => x.Description);
        _validator.TestValidate(Valid with { Description = new string('a', Product.MaxDescriptionLength) }).ShouldNotHaveValidationErrorFor(x => x.Description);
        _validator.TestValidate(Valid with { Description = new string('a', Product.MaxDescriptionLength + 1) }).ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("10000000000.00")]
    public void Price_NotPositive_WithHalfCents_OrTooBig_IsInvalid(string price) =>
        _validator.TestValidate(Valid with { Price = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) }).ShouldHaveValidationErrorFor(x => x.Price);

    [Theory]
    [InlineData("0.01")]
    [InlineData("1.10")]
    [InlineData("1.1")]
    [InlineData("1")]
    [InlineData("9999999999.99")]
    public void Price_OfRealMoney_IsValid(string price) =>
        _validator.TestValidate(Valid with { Price = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) }).ShouldNotHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void Name_Empty_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name));

    [Fact]
    public void EveryBadFieldIsReported_NotOnlyTheFirst()
    {
        var result = _validator.TestValidate(Valid with { Name = "", Price = 0m });

        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void WhatTheValidatorAccepts_TheEntityAccepts()
    {
        // The two must agree, or a command that passes validation becomes a 500 from the constructor.
        foreach (var price in new[] { 0.01m, 1.10m, 1m, Product.MaxPrice })
        {
            var command = Valid with { Price = price };
            _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
            _ = new Product(Guid.CreateVersion7(), command.StoreId, command.Name, command.Description, command.Price);
        }
    }
}
