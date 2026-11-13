using FluentValidation.TestHelper;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class SetLowStockThresholdCommandValidatorTests
{
    private readonly SetLowStockThresholdCommandValidator _validator = new();

    private static SetLowStockThresholdCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 5);

    [Fact]
    public void ACompleteCommand_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(InventoryItem.MaxLowStockThreshold)]
    public void ALevelFromZeroToTheMaximum_IsValid(int level) =>
        _validator.TestValidate(Valid with { Threshold = level }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void NoLevel_IsValid_BecauseItClearsTheLevel() =>
        _validator.TestValidate(Valid with { Threshold = null }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(-1)]
    [InlineData(InventoryItem.MaxLowStockThreshold + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ALevelOutsideTheRange_IsInvalid(int level) =>
        _validator.TestValidate(Valid with { Threshold = level }).ShouldHaveValidationErrorFor(x => x.Threshold);

    [Fact]
    public void AnEmptyStoreId_IsInvalid() =>
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);

    [Fact]
    public void AnEmptyProductId_IsInvalid() =>
        _validator.TestValidate(Valid with { ProductId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ProductId);

    [Fact]
    public void AnEmptyActorId_IsInvalid() =>
        _validator.TestValidate(Valid with { ActorId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ActorId);
}
