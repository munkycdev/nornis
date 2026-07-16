using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// API-side trigger for the relationship backfill sweep: enumerates the world's processed
/// sources and enqueues one worker message per source not yet swept.
/// </summary>
public interface IRelationshipBackfillQueueService
{
    Task<AppResult<BackfillQueueResult>> QueueBackfillAsync(Guid worldId, WorldRole role, CancellationToken ct);
}

public record BackfillQueueResult(int QueuedCount, int AlreadySweptCount, int TotalEligible);
