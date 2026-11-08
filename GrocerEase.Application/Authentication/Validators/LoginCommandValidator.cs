using FluentValidation;
using GrocerEase.Application.Authentication.Commands;

namespace GrocerEase.Application.Authentication.Validators;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Only "is there something to check". The password length policy applies when choosing a
        // password, not when logging in: repeating it here would tell an attacker the policy and
        // would lock out anyone whose password predates a policy change.
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RegisterCommandValidator.MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(RegisterCommandValidator.MaxPasswordLength);
    }
}
