namespace Nornis.Web.ApiClient;

// Client-owned mirrors of the nornis-api JSON contracts. The Web is a separate deployable,
// so it owns its view of the wire shape rather than referencing the API's types. Enum-valued
// fields are carried as strings, exactly as the API serializes them.

public record WorldSummary(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    string MyRole,
    decimal? DailyAiBudgetUsd = null);

public record CreateWorldRequest(
    string Name,
    string? Description,
    string? GameSystem);

public record UpdateWorldRequest(
    string Name,
    string? Description,
    string? GameSystem,
    decimal? DailyAiBudgetUsd = null,
    bool ClearDailyAiBudget = false);

public record WorldMember(
    Guid Id,
    Guid WorldId,
    Guid UserId,
    string Role,
    string? DisplayName,
    DateTimeOffset JoinedAt);

public record AddMemberRequest(
    Guid UserId,
    string Role);

public record UpdateMemberRoleRequest(
    string Role);

public record SourceListItem(
    Guid Id,
    Guid WorldId,
    string Type,
    string Title,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus,
    Guid? CampaignId = null,
    string? CampaignName = null);

public record SourceDetailDto(
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
    string ProcessingStatus,
    Guid? CampaignId = null,
    string? CampaignName = null);

public record CreateSourceRequest(
    string Title,
    string Type,
    string Visibility,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt,
    Guid? CampaignId = null);

// Mirrors Nornis.Api UpdateSourceRequest: every field is optional and only non-null
// fields are applied server-side (partial update).
public record UpdateSourceRequest(
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    string? Type = null,
    string? Visibility = null,
    Guid? CampaignId = null,
    bool ClearCampaign = false);

public record CampaignDto(
    Guid Id,
    Guid WorldId,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CreatedByUserId);

public record CreateCampaignRequest(
    string Name,
    string? Description = null,
    string? Status = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);

public record UpdateCampaignRequest(
    string? Name = null,
    string? Description = null,
    string? Status = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);

public record CharacterDto(
    Guid Id,
    Guid WorldId,
    Guid WorldMemberId,
    string Name,
    string? Description,
    Guid? ArtifactId,
    IReadOnlyList<Guid> CampaignIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateCharacterRequest(
    string Name,
    string? Description = null,
    Guid? WorldMemberId = null,
    Guid? ArtifactId = null);

public record UpdateCharacterRequest(
    string? Name = null,
    string? Description = null,
    Guid? ArtifactId = null,
    bool UnlinkArtifact = false);

public record AssignCampaignCharactersRequest(
    IReadOnlyCollection<Guid> CharacterIds);

public record ArtifactListItem(
    Guid Id,
    Guid WorldId,
    string Type,
    string Name,
    string? Summary,
    string Status,
    string Visibility,
    decimal? Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ArtifactFactDto(
    Guid Id,
    Guid ArtifactId,
    string Predicate,
    string Value,
    decimal? Confidence,
    string TruthState,
    string Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ArtifactRelationshipDto(
    Guid Id,
    Guid ArtifactAId,
    Guid ArtifactBId,
    string Type,
    string? Description,
    decimal? Confidence,
    string TruthState,
    string Visibility);

public record ConnectedArtifact(
    Guid Id,
    string Name,
    string Type);

public record SourceReferenceDto(
    Guid Id,
    Guid SourceId,
    string TargetType,
    Guid TargetId,
    string? Quote,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? SourceTitle = null);

public record ArtifactDetailDto(
    Guid Id,
    Guid WorldId,
    string Type,
    string Name,
    string? Summary,
    string Status,
    string Visibility,
    decimal? Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ArtifactFactDto> Facts,
    IReadOnlyList<ArtifactRelationshipDto> Relationships,
    IReadOnlyList<ConnectedArtifact> ConnectedArtifacts,
    IReadOnlyList<SourceReferenceDto> SourceReferences);

public record CanonEntry(
    string Kind,
    Guid Id,
    Guid ArtifactId,
    string ArtifactName,
    Guid? OtherArtifactId,
    string? OtherArtifactName,
    string Label,
    string? Detail,
    decimal? Confidence,
    string TruthState,
    string Visibility,
    DateTimeOffset UpdatedAt);

public record ReviewProposal(
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
    string? MergeSourceName = null);

public record ReviewQueue(
    IReadOnlyList<ReviewProposal> Proposals,
    bool HasMore);

/// <summary>
/// Result of accept/reject/edit on a proposal. The API returns slightly different shapes per
/// action; this superset captures what the UI needs (extra fields are ignored, absent ones null).
/// </summary>
public record ProposalActionResult(
    Guid ProposalId,
    string Status,
    string? ProposedValueJson,
    Guid? CreatedEntityId);

public record BatchOperationResult(
    IReadOnlyList<Guid> Succeeded,
    IReadOnlyList<BatchFailureItem> Failed);

public record BatchFailureItem(
    Guid ProposalId,
    string Code,
    string Message);

public record CostSummary(
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    decimal TotalEstimatedCostUsd,
    int OperationCount);

public record TimePeriodSummary(
    CostSummary Today,
    CostSummary ThisWeek,
    CostSummary ThisMonth,
    CostSummary AllTime,
    decimal? DailyBudgetUsd = null);

public record UserCost(Guid UserId, string Username, CostSummary Summary);

public record OperationTypeCost(string OperationType, CostSummary Summary);

public record ModelCost(string Model, CostSummary Summary);

public record WorldCost(Guid WorldId, string WorldName, CostSummary Summary);

public record AskRequest(string Question, string? ConversationContext);

public record AskSuggestion(string Text, string Category);

public record Citation(
    string ReferenceId,
    string Type,
    string DisplayName,
    Guid? ArtifactId,
    Guid? FactId,
    Guid? RelationshipId,
    Guid? SourceId);

public record AskAnswer(
    string Answer,
    IReadOnlyList<Citation> Citations,
    string Confidence,
    IReadOnlyList<string> Caveats);

/// <summary>
/// AI-assessed continuity health. <see cref="Score"/> is the blended snapshot at assessment time;
/// <see cref="EffectiveScore"/> reflects only the findings still Open. When the world has never
/// been assessed, <see cref="HasData"/> is false.
/// </summary>
public record ContinuityAssessment(
    bool HasData,
    Guid? AssessmentId,
    DateTimeOffset? CreatedAt,
    string? Model,
    int Score,
    int EffectiveScore,
    int HeuristicScore,
    IReadOnlyList<ContinuityFinding> Findings);

public record ContinuityFinding(
    Guid Id,
    string Category,
    string Severity,
    string Summary,
    string? SuggestedAction,
    IReadOnlyList<string> Evidence,
    Guid? ArtifactId,
    string Status);

public record MergeResult(Guid TargetArtifactId);

public record ArtifactGraphDto(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges);

public record GraphNode(Guid Id, string Name, string Type, string Status);

public record GraphEdge(Guid Id, Guid SourceId, Guid TargetId, string Type);

public record SourceActivity(
    int Ready,
    int Queued,
    int Processing,
    int Failed,
    int PendingProposals,
    bool PendingProposalsCapped)
{
    public int InFlight => Ready + Queued + Processing;
}

public record RetrospectiveResult(
    int AssessedCount,
    int ProposedCount,
    Guid? ReviewBatchId);

/// <summary>Problem detail returned by the API on a non-success status.</summary>
public record ApiError(string Code, string Message);

/// <summary>
/// Result of an API call: either a value or an <see cref="ApiError"/>. Keeps call sites from
/// having to catch exceptions for expected failures (validation, auth, unreachable API).
/// </summary>
public record ApiResult<T>(T? Value, ApiError? Error)
{
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Ok(T value) => new(value, null);
    public static ApiResult<T> Fail(ApiError error) => new(default, error);
}
