namespace GrocerEase.Application.Common;

/// <summary>
/// An expected business failure: a stable <paramref name="Code"/> for clients, a readable <paramref name="Description"/>, and its
/// <paramref name="Type"/>. <paramref name="Data"/> is what the client needs to put it right, when there is something: for a change made
/// from an old version, the current state of the thing it tried to change. Application says what it is; Api decides how it is shown.
/// </summary>
public sealed record ResultError(string Code, string Description, ErrorType Type, object? Data = null);
