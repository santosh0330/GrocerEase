using FluentValidation.TestHelper;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class RemoveStoreMemberCommandValidatorTests
{
    private readonly RemoveStoreMemberCommandValidator _validator = new();

    private static RemoveStoreMemberCommand Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyIds_AreInvalid()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { UserId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
