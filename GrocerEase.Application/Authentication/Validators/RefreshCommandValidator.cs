using FluentValidation;
using GrocerEase.Application.Authentication.Commands;

namespace GrocerEase.Application.Authentication.Validators;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    // A real token is 43 characters. The limit is generous, and exists so a huge body cannot make the server hash megabytes.
    // It is also big enough for an access token (a JWT, which grows with the claims it carries): one presented here by mistake
    // must reach the lookup and get the same single answer as every other wrong token, not a different one only because it is long.
    public const int MaxRefreshTokenLength = 2048;

    public RefreshCommandValidator()
    {
        // See the other validators: one message per field.
        RuleLevelCascadeMode = CascadeMode.Stop;

        // Only "is there something to check". Whether the token is real, expired or used is the handler's answer, and it is the
        // same answer for all of them: a caller learns nothing from the difference.
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(MaxRefreshTokenLength);
    }
}
