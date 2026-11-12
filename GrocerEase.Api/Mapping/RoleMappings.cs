using GrocerEase.Application.Roles.Common;
using GrocerEase.Contracts.Roles;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class RoleMappings
{
    public static RoleResponse ToResponse(this RoleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RoleResponse(result.Id, result.Name);
    }
}
