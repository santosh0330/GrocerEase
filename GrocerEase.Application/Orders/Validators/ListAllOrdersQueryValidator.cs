using FluentValidation;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Validators;

public sealed class ListAllOrdersQueryValidator : AbstractValidator<ListAllOrdersQuery>
{
    // By name, never by number: Enum.TryParse would accept "1", or "7" which is no status at all.
    private static readonly string[] StatusNames = Enum.GetNames<OrderStatus>();

    public ListAllOrdersQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        // ?storeId=00000000-0000-0000-0000-000000000000 is a store that cannot exist: a mistake worth saying so, not an empty page.
        // NotEmpty() on a Guid? treats only null as empty, so Guid.Empty would pass: compare it explicitly.
        RuleFor(x => x.StoreId).Must(storeId => storeId != Guid.Empty).When(x => x.StoreId is not null).WithMessage("'{PropertyName}' must not be empty.");
        RuleFor(x => x.Status)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Status is not null)
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", StatusNames)}.");
        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
