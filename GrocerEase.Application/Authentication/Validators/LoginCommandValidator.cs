using FluentValidation;
using GrocerEase.Application.Authentication.Commands;

namespace GrocerEase.Application.Authentication.Validators;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // A rule stops at its first failing check, so an empty email says "must not be empty" and not also
        // "not a valid email". Different fields are still all checked, so every bad field is reported.
        RuleLevelCascadeMode = CascadeMode.Stop;

        // Only "is there something to check". The password length policy applies when choosing a
        // password, not when logging in: repeating it here would tell an attacker the policy and
        // would lock out anyone whose password predates a policy change.
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RegisterCommandValidator.MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(RegisterCommandValidator.MaxPasswordLength);
    }
}
