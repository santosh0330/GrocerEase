using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Contracts.Authentication;

namespace GrocerEase.Api.Mapping;

/// <summary>Api is the only layer that knows both the Contracts DTOs and the Application types, so it does the mapping.</summary>
public static class AuthenticationMappings
{
    public static RegisterCommand ToCommand(this RegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RegisterCommand(request.FirstName, request.LastName, request.Email, request.Password);
    }

    public static LoginCommand ToCommand(this LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new LoginCommand(request.Email, request.Password);
    }

    public static AuthenticationResponse ToResponse(this AuthenticationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AuthenticationResponse(result.Id, result.FirstName, result.LastName, result.Email, result.Token);
    }
}
