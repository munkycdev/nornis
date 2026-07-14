namespace Nornis.Api.Contracts.Responses;

public record SourceReferenceResponse(
    Guid Id,
    Guid SourceId,
    string TargetType,
    Guid TargetId,
    string? Quote,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? SourceTitle = null);
