namespace GrocerEase.Application.Roles.Common;

/// <summary>What the list-roles handler returns for one role, with the permissions it grants. The Api layer maps it to its response DTO.</summary>
public sealed record RoleResult(Guid Id, string Name, IReadOnlyList<string> Permissions);
