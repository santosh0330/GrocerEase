using GrocerEase.Application.Authentication.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>Issues the token a client sends back to prove who it is. Infrastructure implements it.</summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Makes a token for the user, carrying the role they hold, and says when it expires: the same instant as the token's <c>exp</c>, to the
    /// second. <paramref name="role"/> must be the role the user's <c>RoleId</c> points at.
    /// </summary>
    IssuedAccessToken GenerateToken(User user, Role role);
}
