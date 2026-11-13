using System.Linq.Expressions;
using FluentValidation;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Products.Validators;

/// <summary>The rules for a product's own fields, said once for the command that adds one and the command that edits one. The same limits the entity and the table enforce.</summary>
internal static class ProductRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> name,
        Expression<Func<T, string>> description,
        Expression<Func<T, decimal>> price)
    {
        validator.RuleFor(name).NotEmpty().MaximumLength(Product.MaxNameLength);
        validator.RuleFor(description).NotNull().MaximumLength(Product.MaxDescriptionLength);

        // Greater than zero, at most what the column holds, and no half cents: the entity refuses all three, so a client gets a 400 with
        // a message and not a 500 from a constructor.
        validator.RuleFor(price)
            .GreaterThan(0)
            .LessThanOrEqualTo(Product.MaxPrice)
            .PrecisionScale(precision: 12, scale: Product.PriceScale, ignoreTrailingZeros: true);
    }
}
