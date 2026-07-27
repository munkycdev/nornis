using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The campaign backlog import: gather the notes, order them, then walk them one at a time.
/// Notes come from either direction — typed in fresh, or staged from sources the world
/// already holds, which is how a whole world gets re-extracted in an order the GM chose.
/// GM-only throughout; every method takes the acting user's world role and enforces it here
/// rather than at the controller.
/// </summary>
public interface IImportSessionService
{
    Task<AppResult<ImportSessionInfo>> CreateAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>The world's non-terminal session, or a 404 error when there is none.</summary>
    Task<AppResult<ImportSessionInfo>> GetCurrentAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    Task<AppResult<ImportSessionInfo>> AddItemAsync(AddImportNoteCommand command, CancellationToken ct);

    /// <summary>Reorders the not-yet-started items. <paramref name="orderedItemIds"/> must be
    /// exactly the set of items whose source is still Draft — started items keep their place.</summary>
    Task<AppResult<ImportSessionInfo>> ReorderAsync(
        Guid worldId, Guid sessionId, IReadOnlyList<Guid> orderedItemIds,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Sources in this world that could be staged, in story order, each carrying
    /// whether it is already in the run and how much canon it has already produced.</summary>
    Task<AppResult<IReadOnlyList<ImportCandidateInfo>>> ListCandidatesAsync(
        Guid worldId, Guid sessionId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Stages sources the world already holds, appended in story order for the GM to
    /// rearrange. Ids already in the run are ignored rather than duplicated.</summary>
    Task<AppResult<ImportSessionInfo>> AddExistingSourcesAsync(
        Guid worldId, Guid sessionId, IReadOnlyList<Guid> sourceIds,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Drops an item from the run. Pure queue edit: the source is untouched whatever
    /// its status. This is how a note is excluded — <see cref="DeleteItemAsync"/> is not.</summary>
    Task<AppResult<ImportSessionInfo>> RemoveItemAsync(
        Guid worldId, Guid sessionId, Guid itemId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Removes a not-yet-started item AND deletes its source. Refused unless this
    /// flow created that note: a staged existing source is the GM's own record.</summary>
    Task<AppResult<ImportSessionInfo>> DeleteItemAsync(
        Guid worldId, Guid sessionId, Guid itemId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    Task<AppResult<ImportSessionInfo>> StartAsync(
        Guid worldId, Guid sessionId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Moves to the next note. Permitted only when the current item is done, or when
    /// <paramref name="skipCurrent"/> passes it over. Never automatic — the pacing is the feature.</summary>
    /// <param name="expectedItemId">The item the caller believes is current. When supplied and the
    /// walk has moved on, the call is refused rather than acting on a note the user never saw —
    /// a skip is destructive to the pacing and must not land on the wrong note.</param>
    Task<AppResult<ImportSessionInfo>> AdvanceAsync(
        Guid worldId, Guid sessionId, bool skipCurrent, Guid? expectedItemId,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Retires the session. Deletes nothing: processed notes keep their knowledge and
    /// held notes remain ordinary draft sources.</summary>
    Task<AppResult<ImportSessionInfo>> AbandonAsync(
        Guid worldId, Guid sessionId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);
}
