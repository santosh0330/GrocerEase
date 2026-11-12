namespace GrocerEase.Domain.Entities;

/// <summary>
/// The ids of the seeded roles. They are fixed, not generated, because the migration that creates the roles and every
/// environment's database must agree on them: a user row points at its role by this id.
/// </summary>
public static class RoleIds
{
    // Valid version 7 UUIDs (the third group starts with 7, the fourth with 8), made readable on purpose.
    public static readonly Guid Customer = new("019e0000-0000-7000-8000-000000000001");

    public static readonly Guid Admin = new("019e0000-0000-7000-8000-000000000002");
}
