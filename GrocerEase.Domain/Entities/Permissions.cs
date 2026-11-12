namespace GrocerEase.Domain.Entities;

/// <summary>
/// The things a role can be allowed to do. One definition, used by the seed, the authorization policies and the tests. They are
/// <c>const</c> so <c>[Authorize(Policy = Permissions.RolesRead)]</c> can name one. A permission is stored as a claim of type
/// <see cref="ClaimType"/>, and goes into the token as a claim with that name.
/// </summary>
public static class Permissions
{
    /// <summary>The claim type that means "this role is allowed to do <c>Value</c>".</summary>
    public const string ClaimType = "permission";

    /// <summary>Reading one's own profile, <c>GET /me</c>.</summary>
    public const string ProfileRead = "profile.read";

    /// <summary>Listing the roles, <c>GET /roles</c>.</summary>
    public const string RolesRead = "roles.read";

    /// <summary>Listing the stores and reading one, <c>GET /stores</c>.</summary>
    public const string StoresRead = "stores.read";

    /// <summary>Creating a store and choosing who runs it: <c>POST /stores</c> and the store's members.</summary>
    public const string StoresManage = "stores.manage";

    public static IReadOnlyList<string> All { get; } = [ProfileRead, RolesRead, StoresRead, StoresManage];
}
