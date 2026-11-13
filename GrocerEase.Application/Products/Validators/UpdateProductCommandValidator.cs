using FluentValidation;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Domain.Entities;

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

        // A change is made on top of a version the person read. A client that sends none (an old one, or a mistake) is refused, never applied
        // blindly: the whole point is that nobody overwrites what they have not seen. Missing arrives here as zero.
        RuleFor(x => x.Version)
            .GreaterThanOrEqualTo(Product.FirstVersion)
            .WithMessage("The version of the product you are changing must be sent: use the 'version' you read.");
    }
}
