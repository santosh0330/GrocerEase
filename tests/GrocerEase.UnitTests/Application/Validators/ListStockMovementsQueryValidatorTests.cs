using FluentValidation.TestHelper;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Inventory.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class ListStockMovementsQueryValidatorTests
{
    private readonly ListStockMovementsQueryValidator _validator = new();

    private static ListStockMovementsQuery Valid => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Paging.DefaultPageSize);

    [Fact]
    public void TheDefaultQuery_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyIds_AreInvalid()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { ProductId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ProductId);
        _validator.TestValidate(Valid with { ActorId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ActorId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Paging.MaxPage + 1)]
    public void APageOutsideTheLimits_IsInvalid(int page) =>
        _validator.TestValidate(Valid with { Page = page }).ShouldHaveValidationErrorFor(x => x.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(Paging.MaxPageSize + 1)]
    public void APageSizeOutsideTheLimits_IsInvalid(int size) =>
        _validator.TestValidate(Valid with { PageSize = size }).ShouldHaveValidationErrorFor(x => x.PageSize);
}
