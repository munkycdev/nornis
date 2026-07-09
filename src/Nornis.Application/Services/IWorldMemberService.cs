using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface IWorldMemberService
{
    Task<AppResult<WorldMember>> AddMemberAsync(AddMemberCommand command, CancellationToken ct);
    Task<AppResult> RemoveMemberAsync(Guid worldId, Guid targetUserId, Guid actingUserId, CancellationToken ct);
    Task<AppResult<WorldMember>> UpdateRoleAsync(UpdateMemberRoleCommand command, CancellationToken ct);
    Task<AppResult<IReadOnlyList<WorldMember>>> ListMembersAsync(Guid worldId, Guid requestingUserId, CancellationToken ct);
}
