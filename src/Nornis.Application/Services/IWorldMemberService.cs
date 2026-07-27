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

    /// <summary>
    /// Sets the acting member's own display name in a world. Empty or whitespace clears
    /// it, falling back to the generated user label in UIs.
    /// </summary>
    Task<AppResult<WorldMember>> UpdateDisplayNameAsync(Guid worldId, Guid actingUserId, string? displayName, CancellationToken ct);

    /// <summary>
    /// Users a GM could still add to this world, matching <paramref name="search"/> — the
    /// add-member picker's candidates.
    ///
    /// <para>This is the only query in the application that reads across the user table rather
    /// than within a world, so the GM check lives here rather than only at the controller, the
    /// same way <see cref="AddMemberAsync"/> re-checks what its controller already checked. A
    /// search term is <b>required</b>: there is no "list everyone" mode to fall into, because a
    /// caller can always make themselves a GM of a world they just created, and an unbounded
    /// listing behind that check would be the old open directory with extra steps.</para>
    /// </summary>
    Task<AppResult<IReadOnlyList<User>>> SearchAddableUsersAsync(
        Guid worldId, Guid actingUserId, string? search, CancellationToken ct);
}
