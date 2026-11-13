using FluentValidation;
using GrocerEase.Application.Orders.Commands;

namespace GrocerEase.Application.Orders.Validators;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
