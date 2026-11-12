using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>Issues the token a client sends back to prove who it is. Infrastructure implements it.</summary>
public interface IJwtTokenGenerator
{
    /// <summary>Makes a token for the user, carrying the role they hold. <paramref name="role"/> must be the role the user's <c>RoleId</c> points at.</summary>
    string GenerateToken(User user, Role role);
}
