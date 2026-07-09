using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

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
    Task<AppResult<Source>> MarkReadyAsync(MarkSourceReadyCommand command, CancellationToken ct);
}
