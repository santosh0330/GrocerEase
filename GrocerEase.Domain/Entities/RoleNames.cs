namespace GrocerEase.Domain.Entities;

/// <summary>
/// The names of the roles the application knows. One definition, used by the seed, the code that registers a user, the
/// <c>[Authorize(Roles = ...)]</c> guards and the tests. They are <c>const</c> so an attribute can use them.
/// </summary>
public static class RoleNames
{
    /// <summary>The role every new user gets.</summary>
    public const string Customer = "Customer";

    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Customer, Admin];
}
