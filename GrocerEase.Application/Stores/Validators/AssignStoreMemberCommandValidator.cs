using FluentValidation;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Validators;

public sealed class AssignStoreMemberCommandValidator : AbstractValidator<AssignStoreMemberCommand>
{
    public AssignStoreMemberCommandValidator()
    {
        // A rule stops at its first failing check, so an empty email says "must not be empty" and not also "not a valid email".
        RuleLevelCascadeMode = CascadeMode.Stop;

        // The route holds a Guid, so the only bad value left is the empty one, which no store has.
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.MaxEmailLength);
    }
}
