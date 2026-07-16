using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Worker-side processor for the storyline relationship backfill sweep: one message per
/// already-processed source, producing a review batch of Advances/PartOf link proposals.
/// </summary>
public interface IRelationshipBackfillService
{
    Task<ExtractionOutcome> ProcessBackfillAsync(Guid sourceId, Guid worldId, CancellationToken ct);
}
