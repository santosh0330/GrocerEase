using FluentValidation;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Validators;

public sealed class SetLowStockThresholdCommandValidator : AbstractValidator<SetLowStockThresholdCommand>
{
    public SetLowStockThresholdCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();

        // No level (null) is valid: it clears the level. A level is from 0 ("tell me when it is gone") to the most a product may hold.
        RuleFor(x => x.Threshold).InclusiveBetween(0, InventoryItem.MaxLowStockThreshold).When(x => x.Threshold is not null);
    }
}
