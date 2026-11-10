using GrocerEase.Application.Common;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>The expected failures of registering and logging in. Codes are stable: clients may switch on them.</summary>
public static class AuthenticationErrors
{
    public static readonly ResultError DuplicateEmail = new(
        "User.DuplicateEmail", "The email is already registered.", ErrorType.Conflict);

    // One error for "no such email" and "wrong password", so a caller cannot probe which emails are registered.
    public static readonly ResultError InvalidCredentials = new(
        "Authentication.InvalidCredentials", "The email or password is wrong.", ErrorType.Unauthorized);

    // One error for a token that is unknown, expired, already used or revoked, so a caller cannot tell which it was. The client's
    // only possible reaction is the same in every case: sign in again.
    public static readonly ResultError InvalidRefreshToken = new(
        "Authentication.InvalidRefreshToken", "The refresh token is invalid, expired or already used. Sign in again.", ErrorType.Unauthorized);
}
