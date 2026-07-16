namespace Nornis.Api.Contracts.Responses;

public record ReviewProposalResponse(
    Guid Id,
    Guid ReviewBatchId,
    string ChangeType,
    string TargetType,
    Guid? TargetId,
    string ProposedValueJson,
    string? Rationale,
    decimal? Confidence,
    string Status,
    DateTimeOffset CreatedAt,
    Guid? SourceId = null,
    string? SourceTitle = null,
    string? TargetName = null,
    string? MergeSourceName = null,
    string? BatchKind = null);
