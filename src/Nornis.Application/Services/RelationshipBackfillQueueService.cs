using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// GM-triggered entry point for the relationship backfill sweep. Enqueues one worker
/// message per processed, not-yet-swept source; the worker does the AI work per source
/// (see <see cref="RelationshipBackfillService"/>). Re-running is safe: swept sources are
/// counted and skipped, so an interrupted sweep (e.g. daily AI budget hit) resumes.
/// </summary>
public class RelationshipBackfillQueueService : IRelationshipBackfillQueueService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IExtractionQueueClient _queueClient;
    private readonly ILogger<RelationshipBackfillQueueService> _logger;

    public RelationshipBackfillQueueService(
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IExtractionQueueClient queueClient,
        ILogger<RelationshipBackfillQueueService> logger)
    {
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _queueClient = queueClient;
        _logger = logger;
    }

    public async Task<AppResult<BackfillQueueResult>> QueueBackfillAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<BackfillQueueResult>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can run a relationship backfill."));
        }

        var eligible = (await _sourceRepository.ListByWorldAsync(worldId, null, ct))
            .Where(s => s.ProcessingStatus == SourceProcessingStatus.Processed
                && !string.IsNullOrWhiteSpace(s.Body))
            .ToList();

        var queued = 0;
        var alreadySwept = 0;

        foreach (var source in eligible)
        {
            if (await _reviewBatchRepository.ExistsForSourceAsync(source.Id, RelationshipBackfillService.BatchKind, ct))
            {
                alreadySwept++;
                continue;
            }

            await _queueClient.SendExtractionMessageAsync(source.Id, worldId, ct, ExtractionKind.RelationshipBackfill);
            queued++;
        }

        _logger.LogInformation(
            "Relationship backfill queued. WorldId={WorldId}, Queued={Queued}, AlreadySwept={AlreadySwept}, Eligible={Eligible}",
            worldId, queued, alreadySwept, eligible.Count);

        return AppResult<BackfillQueueResult>.Success(new BackfillQueueResult(queued, alreadySwept, eligible.Count));
    }
}
