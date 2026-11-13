using FluentValidation.TestHelper;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Application.Orders.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class ListMyOrdersQueryValidatorTests
{
    private readonly ListMyOrdersQueryValidator _validator = new();

    private static ListMyOrdersQuery Valid => new(Guid.CreateVersion7(), 1, Paging.DefaultPageSize);

    [Fact]
    public void TheDefaultQuery_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyCustomerId_IsInvalid() =>
        _validator.TestValidate(Valid with { CustomerId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.CustomerId);

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
