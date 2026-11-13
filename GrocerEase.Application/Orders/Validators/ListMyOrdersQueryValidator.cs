using FluentValidation;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Queries;

namespace GrocerEase.Application.Orders.Validators;

public sealed class ListMyOrdersQueryValidator : AbstractValidator<ListMyOrdersQuery>
{
    public ListMyOrdersQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
