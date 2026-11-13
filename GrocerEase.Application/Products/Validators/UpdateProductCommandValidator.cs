using FluentValidation;
using GrocerEase.Application.Products.Commands;

namespace GrocerEase.Application.Products.Validators;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        ProductRules.Apply(this, x => x.Name, x => x.Description, x => x.Price);
    }
}
