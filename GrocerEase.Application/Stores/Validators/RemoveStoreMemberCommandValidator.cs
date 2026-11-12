using FluentValidation;
using GrocerEase.Application.Stores.Commands;

namespace GrocerEase.Application.Stores.Validators;

public sealed class RemoveStoreMemberCommandValidator : AbstractValidator<RemoveStoreMemberCommand>
{
    public RemoveStoreMemberCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        // The route holds Guids, so the only bad value left is the empty one, which no store and no user has.
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
