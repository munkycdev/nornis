namespace Nornis.Api.Contracts.Responses;

public record ExtractionReplayResponse(
    Guid Id,
    string Status,
    Guid CurrentSourceId,
    string? CurrentSourceTitle,
    string? CurrentSourceProcessingStatus,
    int RemainingCount,
    DateTimeOffset CreatedAt);

/// <summary>GET wrapper: Replay is null when the world has no replay in progress, so the
/// endpoint always answers 200 and polling clients need no 404 special case.</summary>
public record ExtractionReplayStateResponse(ExtractionReplayResponse? Replay);

/// <summary>How many sources a replay from a given note would walk, for the confirm dialog.</summary>
public record ExtractionReplayPreviewResponse(int TotalSources);
