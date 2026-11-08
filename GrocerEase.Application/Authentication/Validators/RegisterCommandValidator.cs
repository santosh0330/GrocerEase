using FluentValidation;
using GrocerEase.Application.Authentication.Commands;

namespace GrocerEase.Application.Authentication.Validators;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MinPasswordLength = 8;
    // Long enough for passphrases, short enough that hashing one cannot be used to burn CPU.
    public const int MaxPasswordLength = 128;

    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(MaxNameLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(MaxNameLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(MinPasswordLength).MaximumLength(MaxPasswordLength);
    }
}
