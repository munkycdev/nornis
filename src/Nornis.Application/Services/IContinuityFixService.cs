using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Turns one open continuity finding into concrete review-queue proposals that address every
/// evidence leg the finding cites — retire the losing fact, rewrite the drifted summary,
/// settle the relationship — in a single drafted batch. Nothing changes canon until the GM
/// accepts the proposals in the review queue.
/// </summary>
public interface IContinuityFixService
{
    Task<AppResult<ContinuityFixDraft>> DraftFixAsync(
        Guid worldId, Guid findingId, Guid actingUserId, CancellationToken ct);
}
