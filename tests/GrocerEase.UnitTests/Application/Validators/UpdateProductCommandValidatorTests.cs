using FluentValidation.TestHelper;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class UpdateProductCommandValidatorTests
{
    private readonly UpdateProductCommandValidator _validator = new();

    private static UpdateProductCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Milk", "Fresh", 2.50m, 1);

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyIds_AreInvalid()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { ProductId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ProductId);
        _validator.TestValidate(Valid with { ActorId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ActorId);
    }

    [Fact]
    public void TheProductFields_FollowTheSameRulesAsWhenAddingOne()
    {
        _validator.TestValidate(Valid with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);
        _validator.TestValidate(Valid with { Name = new string('a', Product.MaxNameLength + 1) }).ShouldHaveValidationErrorFor(x => x.Name);
        _validator.TestValidate(Valid with { Description = new string('a', Product.MaxDescriptionLength + 1) }).ShouldHaveValidationErrorFor(x => x.Description);
        _validator.TestValidate(Valid with { Price = 0m }).ShouldHaveValidationErrorFor(x => x.Price);
        _validator.TestValidate(Valid with { Price = 1.005m }).ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void AVersionOfOneOrMore_IsValid(int version) =>
        _validator.TestValidate(Valid with { Version = version }).ShouldNotHaveValidationErrorFor(x => x.Version);

    // Zero is what a body that sent no version becomes: refused, never applied blindly.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AMissingOrNonsenseVersion_IsInvalid_AndTheMessageSaysWhatToSend(int version) =>
        _validator.TestValidate(Valid with { Version = version }).ShouldHaveValidationErrorFor(x => x.Version)
            .WithErrorMessage("The version of the product you are changing must be sent: use the 'version' you read.");
}
