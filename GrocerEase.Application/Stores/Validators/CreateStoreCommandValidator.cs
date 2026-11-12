using FluentValidation;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Stores.Validators;

public sealed class CreateStoreCommandValidator : AbstractValidator<CreateStoreCommand>
{
    public CreateStoreCommandValidator()
    {
        // A rule stops at its first failing check, so an empty name says "must not be empty" and not also "too long".
        RuleLevelCascadeMode = CascadeMode.Stop;

        // The same limits the entity and the table enforce, so a client gets a 400 with a message and not a 500 from a constructor.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Store.MaxNameLength);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(Store.MaxAddressLength);
    }
}
