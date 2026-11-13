using FluentValidation;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Queries;

namespace GrocerEase.Application.Stores.Validators;

public sealed class ListStoresQueryValidator : AbstractValidator<ListStoresQuery>
{
    public ListStoresQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
