namespace GrocerEase.Contracts.Users;

/// <summary>Who the signed-in user is, what role they hold and what that role lets them do, as the API knows it from their token. Nothing secret: no token, no password hash.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    IReadOnlyList<string> Permissions);
