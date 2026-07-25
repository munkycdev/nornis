namespace Nornis.Application.Models;

/// <summary>
/// Read model for a timeline replay: where the walk currently stands. The cursor source's
/// processing status tells the GM what the replay is waiting on — Queued/Processing means
/// extraction is running, Processed means its proposals await review, Failed means the
/// walk is stalled until the source is retried or the replay canceled.
/// </summary>
public record ExtractionReplayInfo(
    Guid Id,
    string Status,
    Guid CurrentSourceId,
    string? CurrentSourceTitle,
    string? CurrentSourceProcessingStatus,
    int RemainingCount,
    DateTimeOffset CreatedAt);
