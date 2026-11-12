using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Reads roles from PostgreSQL through EF Core. Scoped, like the context it wraps. Nothing here changes a role, so nothing is
/// tracked: EF has no reason to remember what it reads. Every role comes back with its claims: a role without them would look
/// like a role that may do nothing, which is a different thing from a role whose claims were not loaded.
/// </summary>
public sealed class RoleRepository(AppDbContext db) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Roles.AsNoTracking().Include(role => role.Claims).SingleOrDefaultAsync(role => role.Id == id, cancellationToken);

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        // An exact match, which is what the unique index on the name serves.
        return db.Roles.AsNoTracking().Include(role => role.Claims).SingleOrDefaultAsync(role => role.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().Include(role => role.Claims).OrderBy(role => role.Name).ToListAsync(cancellationToken);
}
