using FluentValidation;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Validators;

public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    private static readonly string[] ChoosableReasons = [nameof(StockMovementReason.Received), nameof(StockMovementReason.Correction)];

    public AdjustStockCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();

        // Zero changes nothing, so it is a mistake and not a request. The bounds keep the sum of a quantity and a change inside an int.
        RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-InventoryItem.MaxQuantity, InventoryItem.MaxQuantity);

        // Only the two reasons a person may give, by name (never by number: "3" is not a reason). The others are what an order does, and what
        // the migration did, and nobody can claim them by hand. A delivery always adds: "received -5" is a correction, so say so.
        RuleFor(x => x.Reason)
            .Must(reason => ChoosableReasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Reason is not null)
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", ChoosableReasons)}.");
        RuleFor(x => x.Delta)
            .GreaterThan(0)
            .When(x => string.Equals(x.Reason, nameof(StockMovementReason.Received), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A delivery adds stock: use a positive change, or the reason Correction.");
    }
}
