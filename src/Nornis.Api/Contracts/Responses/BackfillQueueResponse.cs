namespace Nornis.Api.Contracts.Responses;

public record BackfillQueueResponse(
    int QueuedCount,
    int AlreadySweptCount,
    int TotalEligible);
