using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IStorylineRetrospectiveService
{
    /// <summary>
    /// Reads every Active storyline against its recorded facts and proposes status
    /// closures (Resolved/Dormant) as review proposals. Nothing changes canon until
    /// the user accepts.
    /// </summary>
    Task<AppResult<RetrospectiveResult>> RunAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
