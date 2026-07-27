using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Services;

public interface ISourceService
{
    Task<AppResult<Source>> CreateAsync(CreateSourceCommand command, CancellationToken ct);
    Task<AppResult<Source>> GetByIdAsync(Guid sourceId, Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);
    Task<AppResult<Source>> UpdateAsync(UpdateSourceCommand command, CancellationToken ct);
    Task<AppResult> DeleteAsync(Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
    /// <param name="campaignId">Restrict to sources of this campaign.</param>
    /// <param name="unassignedOnly">Restrict to sources with no campaign; ignored when <paramref name="campaignId"/> is set.</param>
    Task<AppResult<IReadOnlyList<Source>>> ListByWorldAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct, Guid? campaignId = null, bool unassignedOnly = false);

    /// <summary>
    /// The world's sources for a list view, projected in SQL. Prefer this over
    /// <see cref="ListByWorldAsync"/> wherever the caller only needs list fields — it leaves the
    /// unbounded body columns in the database.
    /// </summary>
    Task<AppResult<IReadOnlyList<SourceListItem>>> ListSummariesByWorldAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct,
        Guid? campaignId = null, bool unassignedOnly = false);

    /// <summary>
    /// Nav badge counts for a world, computed as aggregates rather than by loading and grouping
    /// the world's sources. See <see cref="SourceActivity"/>.
    /// </summary>
    Task<AppResult<SourceActivity>> GetActivityAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);
    Task<AppResult<Source>> MarkReadyAsync(MarkSourceReadyCommand command, CancellationToken ct);
}
