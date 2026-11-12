namespace GrocerEase.Contracts.Roles;

/// <summary>One role, as <c>GET /roles</c> returns it.</summary>
public sealed record RoleResponse(Guid Id, string Name);
