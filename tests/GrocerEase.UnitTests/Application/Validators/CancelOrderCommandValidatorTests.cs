using FluentValidation.TestHelper;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class CancelOrderCommandValidatorTests
{
    private readonly CancelOrderCommandValidator _validator = new();

    private static CancelOrderCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void ACompleteCommand_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyStoreId_IsInvalid() =>
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);

    [Fact]
    public void AnEmptyOrderId_IsInvalid() =>
        _validator.TestValidate(Valid with { OrderId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.OrderId);

    [Fact]
    public void AnEmptyActorId_IsInvalid() =>
        _validator.TestValidate(Valid with { ActorId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ActorId);
}
