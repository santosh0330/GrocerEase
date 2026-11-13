using FluentValidation.TestHelper;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Application.Orders.Validators;

namespace GrocerEase.UnitTests.Application.Validators;

public class ListAllOrdersQueryValidatorTests
{
    private readonly ListAllOrdersQueryValidator _validator = new();

    private static ListAllOrdersQuery Valid => new(null, null, 1, Paging.DefaultPageSize);

    [Fact]
    public void TheDefaultQuery_IsValid() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AStoreId_IsValid() =>
        _validator.TestValidate(Valid with { StoreId = Guid.CreateVersion7() }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyStoreId_IsInvalid_ButNoStoreIdIsFine()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { StoreId = null }).ShouldNotHaveValidationErrorFor(x => x.StoreId);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("verified")]
    [InlineData("SHIPPED")]
    [InlineData("Cancelled")]
    public void AStatusName_IsValid_InAnyCase(string status) =>
        _validator.TestValidate(Valid with { Status = status }).ShouldNotHaveValidationErrorFor(x => x.Status);

    // A number is not a status, even one that Enum.TryParse would accept ("1" is Verified there) or one that is not a status at all.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Lost")]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("Pending,Verified")]
    public void ThingsThatAreNotAStatusName_AreInvalid_AndTheMessageListsTheNames(string status)
    {
        var result = _validator.TestValidate(Valid with { Status = status });

        result.ShouldHaveValidationErrorFor(x => x.Status).WithErrorMessage("'Status' must be one of: Pending, Verified, Shipped, Cancelled.");
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
