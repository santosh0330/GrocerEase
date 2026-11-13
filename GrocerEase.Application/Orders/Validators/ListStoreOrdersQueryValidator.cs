using FluentValidation;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Validators;

public sealed class ListStoreOrdersQueryValidator : AbstractValidator<ListStoreOrdersQuery>
{
    // By name, never by number: Enum.TryParse would accept "1", or "7" which is no status at all.
    private static readonly string[] StatusNames = Enum.GetNames<OrderStatus>();

    public ListStoreOrdersQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.Status)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Status is not null)
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", StatusNames)}.");
        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
