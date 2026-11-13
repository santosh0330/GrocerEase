using FluentValidation.TestHelper;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class AdjustStockCommandValidatorTests
{
    private readonly AdjustStockCommandValidator _validator = new();

    private static AdjustStockCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 12);

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
    public void AChangeOfZero_IsInvalid_BecauseItChangesNothing() =>
        _validator.TestValidate(Valid with { Delta = 0 }).ShouldHaveValidationErrorFor(x => x.Delta);

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(InventoryItem.MaxQuantity)]
    [InlineData(-InventoryItem.MaxQuantity)]
    public void AChangeWithinTheLimits_InEitherDirection_IsValid(int delta) =>
        _validator.TestValidate(Valid with { Delta = delta }).ShouldNotHaveValidationErrorFor(x => x.Delta);

    [Theory]
    [InlineData(InventoryItem.MaxQuantity + 1)]
    [InlineData(-InventoryItem.MaxQuantity - 1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void AChangeBeyondTheLimits_IsInvalid(int delta) =>
        _validator.TestValidate(Valid with { Delta = delta }).ShouldHaveValidationErrorFor(x => x.Delta);

    [Fact]
    public void AZeroDelta_GivesOneMessage() =>
        Assert.Single(_validator.TestValidate(Valid with { Delta = 0 }).ShouldHaveValidationErrorFor(x => x.Delta));

    [Fact]
    public void NoReason_IsValid_TheSignDecides() =>
        _validator.TestValidate(Valid with { Reason = null }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("Received", 5)]
    [InlineData("received", 5)]
    [InlineData("RECEIVED", 1)]
    [InlineData("Correction", 5)]
    [InlineData("correction", -5)]
    [InlineData("Correction", -1)]
    public void ARealReasonThatFitsTheChange_IsValid(string reason, int delta) =>
        _validator.TestValidate(Valid with { Reason = reason, Delta = delta }).ShouldNotHaveAnyValidationErrors();

    // The reasons an order or the migration give are not for a person to claim, and a reason is a name: never a number.
    [Theory]
    [InlineData("OrderPlaced")]
    [InlineData("OrderCancelled")]
    [InlineData("OpeningBalance")]
    [InlineData("Lost")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("Received,Correction")]
    public void AReasonThatIsNotOneAPersonMayGive_IsInvalid_AndTheMessageListsTheTwo(string reason) =>
        _validator.TestValidate(Valid with { Reason = reason }).ShouldHaveValidationErrorFor(x => x.Reason).WithErrorMessage("'Reason' must be one of: Received, Correction.");

    [Theory]
    [InlineData(-1)]
    [InlineData(-1_000)]
    public void ADeliveryThatRemoves_IsInvalid_BecauseADeliveryAdds(int delta) =>
        _validator.TestValidate(Valid with { Reason = "Received", Delta = delta }).ShouldHaveValidationErrorFor(x => x.Delta);
}
