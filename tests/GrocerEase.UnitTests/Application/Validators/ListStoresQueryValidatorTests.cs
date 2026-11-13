using FluentValidation.TestHelper;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Queries;
using GrocerEase.Application.Stores.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class ListStoresQueryValidatorTests
{
    private readonly ListStoresQueryValidator _validator = new();

    [Fact]
    public void TheDefaultQuery_IsValid() =>
        _validator.TestValidate(new ListStoresQuery(1, Paging.DefaultPageSize)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(Paging.MaxPage + 1)]
    public void APageOutsideTheLimits_IsInvalid(int page) =>
        _validator.TestValidate(new ListStoresQuery(page, 20)).ShouldHaveValidationErrorFor(x => x.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(Paging.MaxPageSize + 1)]
    public void APageSizeOutsideTheLimits_IsInvalid(int size) =>
        _validator.TestValidate(new ListStoresQuery(1, size)).ShouldHaveValidationErrorFor(x => x.PageSize);
}
