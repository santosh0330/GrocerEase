using FluentValidation;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Validators;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        // A rule stops at its first failing check, so an empty list says "must not be empty" and not also "too many".
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.DeliveryAddress).NotEmpty().MaximumLength(Order.MaxDeliveryAddressLength);

        // The same limits the entity refuses, so a client gets a 400 with a message and not a 500 from a constructor.
        RuleFor(x => x.Lines)
            .NotEmpty()
            .Must(lines => lines.Count <= Order.MaxLines).WithMessage($"An order may hold at most {Order.MaxLines} different products.")
            .Must(lines => lines.Select(line => line.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("A product may appear only once: ask for a bigger quantity instead.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, InventoryItem.MaxQuantity);
        });
    }
}
