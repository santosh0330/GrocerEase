namespace GrocerEase.Contracts.Users;

/// <summary>Who the signed-in user is and what role they hold, as the API knows it from their token. Nothing secret: no token, no password hash.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role);
