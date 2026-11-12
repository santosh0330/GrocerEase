namespace GrocerEase.Contracts.Roles;

/// <summary>One role and the permissions it grants, as <c>GET /roles</c> returns it.</summary>
public sealed record RoleResponse(Guid Id, string Name, IReadOnlyList<string> Permissions);
