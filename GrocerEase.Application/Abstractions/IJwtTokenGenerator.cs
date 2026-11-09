using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>Issues the token a client sends back to prove who it is. Infrastructure implements it.</summary>
public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
}
