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

    /// <summary>Browsing the products of a store, <c>GET /stores/{storeId}/products</c>.</summary>
    public const string ProductsRead = "products.read";

    /// <summary>Adding, editing and archiving the products of one's own store.</summary>
    public const string ProductsManage = "products.manage";

    /// <summary>Reading and adjusting the stock of one's own store.</summary>
    public const string InventoryManage = "inventory.manage";

    /// <summary>Placing an order, <c>POST /stores/{storeId}/orders</c>.</summary>
    public const string OrdersPlace = "orders.place";

    /// <summary>Reading one's own orders.</summary>
    public const string OrdersRead = "orders.read";

    /// <summary>Seeing the orders of one's own store and moving them along: verify, ship, cancel.</summary>
    public const string OrdersManage = "orders.manage";

    /// <summary>Cancelling one's own order while it is still pending, <c>POST /orders/{orderId}/cancel</c>.</summary>
    public const string OrdersCancel = "orders.cancel";

    /// <summary>Reading the orders of <b>every</b> store, read only: <c>GET /admin/orders</c>. Not the same as <see cref="OrdersManage"/>, which is for the orders of one's own store and moves them along.</summary>
    public const string OrdersReadAll = "orders.read.all";

    public static IReadOnlyList<string> All { get; } =
        [ProfileRead, RolesRead, StoresRead, StoresManage, ProductsRead, ProductsManage, InventoryManage, OrdersPlace, OrdersRead, OrdersManage, OrdersCancel, OrdersReadAll];
}
