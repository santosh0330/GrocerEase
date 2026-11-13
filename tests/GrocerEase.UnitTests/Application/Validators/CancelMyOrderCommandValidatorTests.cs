using FluentValidation.TestHelper;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class CancelMyOrderCommandValidatorTests
{
    private readonly CancelMyOrderCommandValidator _validator = new();

    private static CancelMyOrderCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void ACompleteCommand_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyOrderId_IsInvalid() =>
        _validator.TestValidate(Valid with { OrderId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.OrderId);

    [Fact]
    public void AnEmptyCustomerId_IsInvalid() =>
        _validator.TestValidate(Valid with { CustomerId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.CustomerId);
}
