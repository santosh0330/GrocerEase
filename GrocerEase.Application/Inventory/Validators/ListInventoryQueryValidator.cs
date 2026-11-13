using FluentValidation;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Queries;

namespace GrocerEase.Application.Inventory.Validators;

public sealed class ListInventoryQueryValidator : AbstractValidator<ListInventoryQuery>
{
    public ListInventoryQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
