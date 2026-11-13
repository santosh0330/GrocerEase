using FluentValidation;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Products.Validators;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // A rule stops at its first failing check, so an empty name says "must not be empty" and not also "too long".
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        ProductRules.Apply(this, x => x.Name, x => x.Description, x => x.Price);
    }
}
