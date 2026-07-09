namespace Nornis.Api.Contracts.Responses;

public record SourceResponse(
    Guid Id,
    Guid WorldId,
    string Type,
    string Title,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus);
