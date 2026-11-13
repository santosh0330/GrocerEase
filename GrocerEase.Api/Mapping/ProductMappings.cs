using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Products;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class ProductMappings
{
    public static CreateProductCommand ToCommand(this CreateProductRequest request, Guid storeId, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CreateProductCommand(storeId, actorId, request.Name, request.Description ?? string.Empty, request.Price);
    }

    public static PagedResponse<ProductResponse> ToResponse(this PagedResult<ProductResult> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PagedResponse<ProductResponse>([.. page.Items.Select(product => product.ToResponse())], page.Page, page.PageSize, page.TotalCount);
    }

    public static ProductResponse ToResponse(this ProductResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProductResponse(result.Id, result.StoreId, result.Name, result.Description, result.Price);
    }
}
