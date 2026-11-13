using FluentValidation;
using GrocerEase.Application.Orders.Commands;

namespace GrocerEase.Application.Orders.Validators;

public sealed class ShipOrderCommandValidator : AbstractValidator<ShipOrderCommand>
{
    public ShipOrderCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
