using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Contracts.Stores;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class StoreMappings
{
    public static CreateStoreCommand ToCommand(this CreateStoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CreateStoreCommand(request.Name, request.Address);
    }

    public static StoreResponse ToResponse(this StoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new StoreResponse(result.Id, result.Name, result.Address);
    }

    public static AssignStoreMemberCommand ToCommand(this AssignStoreMemberRequest request, Guid storeId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AssignStoreMemberCommand(storeId, request.Email);
    }

    public static RemoveStoreMemberCommand ToRemoveCommand(this Guid userId, Guid storeId) => new(storeId, userId);

    public static StoreMemberResponse ToResponse(this StoreMemberResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new StoreMemberResponse(result.StoreId, result.UserId, result.FirstName, result.LastName, result.Email);
    }
}
