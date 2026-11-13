using FluentValidation.TestHelper;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Application.Products.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class ListProductsQueryValidatorTests
{
    private readonly ListProductsQueryValidator _validator = new();

    private static ListProductsQuery Valid => new(Guid.CreateVersion7(), 1, Paging.DefaultPageSize);

    [Fact]
    public void ValidQuery_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyStoreId_IsInvalid() =>
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Paging.MaxPage + 1)]
    [InlineData(int.MaxValue)]
    public void APageOutsideOneToTheMaximum_IsInvalid(int page) =>
        _validator.TestValidate(Valid with { Page = page }).ShouldHaveValidationErrorFor(x => x.Page);

    [Theory]
    [InlineData(1)]
    [InlineData(Paging.MaxPage)]
    public void TheEdgesOfThePageRange_AreValid(int page) =>
        _validator.TestValidate(Valid with { Page = page }).ShouldNotHaveValidationErrorFor(x => x.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(Paging.MaxPageSize + 1)]
    public void APageSizeOutsideOneToTheMaximum_IsInvalid(int size) =>
        _validator.TestValidate(Valid with { PageSize = size }).ShouldHaveValidationErrorFor(x => x.PageSize);

    [Theory]
    [InlineData(1)]
    [InlineData(Paging.MaxPageSize)]
    public void TheEdgesOfThePageSizeRange_AreValid(int size) =>
        _validator.TestValidate(Valid with { PageSize = size }).ShouldNotHaveValidationErrorFor(x => x.PageSize);

    [Fact]
    public void TheLargestSkip_FitsInAnInt() =>
        Assert.True((long)(Paging.MaxPage - 1) * Paging.MaxPageSize <= int.MaxValue); // so the repository's (page - 1) * pageSize cannot overflow
}
