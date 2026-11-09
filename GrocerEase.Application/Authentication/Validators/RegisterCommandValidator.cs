using FluentValidation;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Validators;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    // Name and email limits belong to the User entity, which the database columns follow too. The password limits are
    // an input policy, not a column size (the column holds a hash), so they stay here.
    public const int MinPasswordLength = 8;
    // Long enough for passphrases, short enough that hashing one cannot be used to burn CPU.
    public const int MaxPasswordLength = 128;

    public RegisterCommandValidator()
    {
        // A rule stops at its first failing check, so an empty email says "must not be empty" and not also
        // "not a valid email". Different fields are still all checked, so every bad field is reported.
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(User.MaxNameLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(User.MaxNameLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(MinPasswordLength).MaximumLength(MaxPasswordLength);
    }
}
