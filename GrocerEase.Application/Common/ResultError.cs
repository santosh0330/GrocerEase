namespace GrocerEase.Application.Common;

/// <summary>An expected business failure: a stable <paramref name="Code"/> for clients, a readable <paramref name="Description"/>, and its <paramref name="Type"/>.</summary>
public sealed record ResultError(string Code, string Description, ErrorType Type);
