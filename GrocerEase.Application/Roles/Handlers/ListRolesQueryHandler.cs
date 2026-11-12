using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Roles.Common;
using GrocerEase.Application.Roles.Queries;

namespace GrocerEase.Application.Roles.Handlers;

/// <summary>
/// Lists every role. It reuses <see cref="ICommandHandler{TCommand, TResponse}"/> although a read changes nothing: one handler
/// shape keeps the validation decorator and the architecture tests working. A separate query handler is a later refactor.
/// </summary>
public sealed class ListRolesQueryHandler(IRoleRepository roleRepository)
    : ICommandHandler<ListRolesQuery, Result<IReadOnlyList<RoleResult>>>
{
    public async Task<Result<IReadOnlyList<RoleResult>>> Handle(ListRolesQuery command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var roles = await roleRepository.ListAsync(cancellationToken);
        IReadOnlyList<RoleResult> results = [.. roles.Select(role => new RoleResult(role.Id, role.Name, role.GrantedPermissions))];
        return Result.Success(results);
    }
}
