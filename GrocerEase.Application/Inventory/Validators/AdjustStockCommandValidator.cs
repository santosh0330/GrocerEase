using FluentValidation;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Validators;

public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();

        // Zero changes nothing, so it is a mistake and not a request. The bounds keep the sum of a quantity and a change inside an int.
        RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-InventoryItem.MaxQuantity, InventoryItem.MaxQuantity);
    }
}
