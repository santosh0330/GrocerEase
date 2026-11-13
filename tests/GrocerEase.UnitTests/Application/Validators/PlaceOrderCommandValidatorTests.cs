using FluentValidation.TestHelper;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Validators;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application.Validators;

public class PlaceOrderCommandValidatorTests
{
    private readonly PlaceOrderCommandValidator _validator = new();

    private static PlaceOrderCommand Valid => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), "1 High Street", [new OrderLineInput(Guid.CreateVersion7(), 2), new OrderLineInput(Guid.CreateVersion7(), 1)]);

    [Fact]
    public void ValidCommand_HasNoErrors() =>
        _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyIds_AreInvalid()
    {
        _validator.TestValidate(Valid with { StoreId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.StoreId);
        _validator.TestValidate(Valid with { CustomerId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.CustomerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankDeliveryAddress_IsInvalid(string address) =>
        _validator.TestValidate(Valid with { DeliveryAddress = address }).ShouldHaveValidationErrorFor(x => x.DeliveryAddress);

    [Fact]
    public void TheDeliveryAddress_HasAMaximum()
    {
        _validator.TestValidate(Valid with { DeliveryAddress = new string('a', Order.MaxDeliveryAddressLength) }).ShouldNotHaveValidationErrorFor(x => x.DeliveryAddress);
        _validator.TestValidate(Valid with { DeliveryAddress = new string('a', Order.MaxDeliveryAddressLength + 1) }).ShouldHaveValidationErrorFor(x => x.DeliveryAddress);
    }

    [Fact]
    public void AnOrderWithNoLines_IsInvalid() =>
        _validator.TestValidate(Valid with { Lines = [] }).ShouldHaveValidationErrorFor(x => x.Lines);

    [Fact]
    public void TheLines_HaveAMaximumNumber()
    {
        IReadOnlyList<OrderLineInput> Many(int count) => [.. Enumerable.Range(0, count).Select(_ => new OrderLineInput(Guid.CreateVersion7(), 1))];

        _validator.TestValidate(Valid with { Lines = Many(Order.MaxLines) }).ShouldNotHaveValidationErrorFor(x => x.Lines);
        _validator.TestValidate(Valid with { Lines = Many(Order.MaxLines + 1) }).ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void AProductTwice_IsInvalid_WithAMessageSayingToAskForABiggerQuantity()
    {
        var product = Guid.CreateVersion7();

        var result = _validator.TestValidate(Valid with { Lines = [new OrderLineInput(product, 1), new OrderLineInput(product, 2)] });

        Assert.Contains("bigger quantity", Assert.Single(result.ShouldHaveValidationErrorFor(x => x.Lines)).ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InventoryItem.MaxQuantity + 1)]
    public void AQuantityOutsideOneToTheMaximum_IsInvalid(int quantity) =>
        _validator.TestValidate(Valid with { Lines = [new OrderLineInput(Guid.CreateVersion7(), quantity)] }).ShouldHaveValidationErrorFor("Lines[0].Quantity");

    [Theory]
    [InlineData(1)]
    [InlineData(InventoryItem.MaxQuantity)]
    public void TheEdgesOfTheQuantity_AreValid(int quantity) =>
        _validator.TestValidate(Valid with { Lines = [new OrderLineInput(Guid.CreateVersion7(), quantity)] }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AnEmptyProductId_IsInvalid() =>
        _validator.TestValidate(Valid with { Lines = [new OrderLineInput(Guid.Empty, 1)] }).ShouldHaveValidationErrorFor("Lines[0].ProductId");

    [Fact]
    public void EveryBadFieldIsReported_NotOnlyTheFirst()
    {
        var result = _validator.TestValidate(Valid with { DeliveryAddress = "", Lines = [new OrderLineInput(Guid.CreateVersion7(), 0)] });

        result.ShouldHaveValidationErrorFor(x => x.DeliveryAddress);
        result.ShouldHaveValidationErrorFor("Lines[0].Quantity");
    }

    [Fact]
    public void WhatTheValidatorAccepts_TheAggregateAccepts()
    {
        // The two must agree, or a command that passes validation becomes a 500 from a constructor.
        var command = Valid with { Lines = [.. Enumerable.Range(0, Order.MaxLines).Select(_ => new OrderLineInput(Guid.CreateVersion7(), InventoryItem.MaxQuantity))] };
        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
        var id = Guid.CreateVersion7();

        var order = Order.Place(
            id, command.StoreId, command.CustomerId, command.DeliveryAddress, DateTimeOffset.UtcNow,
            command.Lines.Select(line => new OrderLine(Guid.CreateVersion7(), id, line.ProductId, "Milk", 1m, line.Quantity)));

        Assert.Equal(Order.MaxLines, order.Lines.Count);
    }
}
