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
    decimal? DailyAiBudgetUsd = null,
    string? PublicSlug = null,
    bool PublicAccessEnabled = false);

public record CreateWorldRequest(
    string Name,
    string? Description,
    string? GameSystem);

public record UpdateWorldRequest(
    string Name,
    string? Description,
    string? GameSystem,
    decimal? DailyAiBudgetUsd = null,
    bool ClearDailyAiBudget = false,
    string? PublicSlug = null,
    bool? PublicAccessEnabled = null);

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

public record WorldInvite(
    Guid Id,
    Guid WorldId,
    string Code,
    string Role,
    string Status,
    int UseCount,
    int? MaxUses,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public record CreateInviteRequest(
    string Role,
    DateTimeOffset? ExpiresAt = null,
    int? MaxUses = null);

public record InvitePreview(
    Guid WorldId,
    string WorldName,
    string Role,
    string Status);

public record AcceptInviteResult(
    Guid WorldId,
    string WorldName,
    bool AlreadyMember);

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

public record SourceAttachmentDto(
    Guid Id,
    Guid SourceId,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int Ord,
    string Status,
    DateTimeOffset CreatedAt,
    string? Url = null);

public record SourceAttachmentUploadTicketDto(
    SourceAttachmentDto Attachment,
    string UploadUrl);

public record RequestSourceAttachmentUploadRequest(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    int Ord = 0);

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
    string? CampaignName = null,
    bool ExtractionEnabled = true,
    string? DerivedText = null);

// Mirrors Nornis.Api LinkedLocationResponse: one Location a session is linked to.
public record LinkedLocationDto(Guid ArtifactId, string Name, string? Summary);

public record CreateSourceRequest(
    string Title,
    string Type,
    string Visibility,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt,
    Guid? CampaignId = null,
    bool ExtractionEnabled = true);

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
    bool ClearCampaign = false,
    bool? ExtractionEnabled = null);

// Mirrors Nornis.Api ReprocessSourceRequest: edits applied atomically with the
// reprocess; null fields keep current values.
public record ReprocessSourceRequest(
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null);

// Mirrors Nornis.Api MapViewResponse.
public record MapPlacemarkDto(
    Guid Id,
    Guid ArtifactId,
    string ArtifactName,
    decimal X,
    decimal Y,
    string? Label,
    decimal? Confidence);

public record MapViewDto(
    SourceAttachmentDto Attachment,
    string ImageUrl,
    IReadOnlyList<MapPlacemarkDto> Placemarks);

// Mirrors Nornis.Api JourneyResponse.
public record JourneyLocationDto(Guid ArtifactId, string Name, decimal X, decimal Y, string? Label);

public record JourneyHighlightDto(Guid ArtifactId, string Name, string Type, bool FirstSeen, string? Summary);

public record JourneyStopDto(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> VisitedLocationIds,
    IReadOnlyList<JourneyHighlightDto> Highlights);

public record JourneyDto(
    Guid MapAttachmentId,
    string ImageUrl,
    IReadOnlyList<JourneyLocationDto> Locations,
    IReadOnlyList<JourneyStopDto> Stops,
    int UndatedSessionCount);

// Mirrors Nornis.Api ReprocessPreviewResponse.
public record ReprocessPreviewDto(
    IReadOnlyList<string> ArtifactNamesToDelete,
    IReadOnlyList<string> ArtifactNamesToKeep,
    int FactsToDelete,
    int RelationshipsToDelete,
    int PendingProposalsToDiscard,
    int MapPinsToDelete = 0);

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
    string Type,
    string? Summary = null);

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
    IReadOnlyList<SourceReferenceDto> SourceReferences,
    IReadOnlyList<string>? PlayedBy = null,
    IReadOnlyList<DeclaredCampaignDto>? DeclaredCampaigns = null);

/// <summary>A campaign a storyline is declared to belong to (id + name only).</summary>
public record DeclaredCampaignDto(Guid Id, string Name);

public record RevealBody(
    IReadOnlyList<Guid> ArtifactIds,
    IReadOnlyList<Guid> FactIds,
    IReadOnlyList<Guid> RelationshipIds,
    IReadOnlyList<RevealCorrectionBody> Corrections,
    string? Note);

public record RevealCorrectionBody(Guid FactId, string TruthState);

public record RevealResponseDto(
    Guid? BatchId,
    int RevealedArtifacts,
    int RevealedFacts,
    int RevealedRelationships,
    int Corrections);

public record RevealNotClosedDto(
    string Code,
    string Message,
    IReadOnlyList<Guid> MissingArtifactIds);

/// <summary>Unified client-side reveal outcome: <c>Applied</c> true = promotion done;
/// <c>Applied</c> false = the set was not reference-closed and <c>MissingArtifactIds</c> must be
/// added before it can be applied.</summary>
public record RevealOutcome(
    bool Applied,
    Guid? BatchId,
    int RevealedArtifacts,
    int RevealedFacts,
    int RevealedRelationships,
    int Corrections,
    IReadOnlyList<Guid> MissingArtifactIds);

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
    string? MergeSourceName = null,
    string? BatchKind = null);

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
    Guid? SourceId,
    Guid? DocumentId = null);

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

public record ArtifactRemovalPreview(
    string ArtifactName,
    string ArtifactType,
    int FactCount,
    IReadOnlyList<string> Relationships,
    int MapPinCount,
    int CharacterLinksToClear);

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

public record UserSummaryDto(Guid Id, string Username);

public record BackfillQueueResult(
    int QueuedCount,
    int AlreadySweptCount,
    int TotalEligible);

public record RetrospectiveResult(
    int AssessedCount,
    int ProposedCount,
    Guid? ReviewBatchId);

// ----------------------------------------------------------- Session wrap-up --

public record WrapUpDto(
    bool HasWork,
    ContinuitySessionRefDto? LatestSession,
    IReadOnlyList<WrapUpAdvancedDto> Advanced,
    IReadOnlyList<QuietStorylineDto> GoneQuiet,
    IReadOnlyList<WrapUpNestSuggestionDto> CouldNest,
    IReadOnlyList<WrapUpUnparentedDto> UnparentedArcs,
    IReadOnlyList<WrapUpParentOptionDto> ParentOptions);

public record ContinuitySessionRefDto(Guid SourceId, string Title, DateTimeOffset OccurredAt);

public record WrapUpAdvancedDto(
    Guid StorylineId, string Name, string Status, int RecentDevelopmentCount, DateTimeOffset LastDevelopmentAt);

public record QuietStorylineDto(
    Guid StorylineId,
    string Name,
    string Status,
    DateTimeOffset? LastDevelopmentAt,
    int SessionsSinceLastDevelopment,
    int OpenQuestionCount,
    Guid? ParentStorylineId);

public record WrapUpNestSuggestionDto(
    Guid ProposalId,
    Guid ChildStorylineId,
    string ChildName,
    Guid ParentStorylineId,
    string ParentName,
    string? Rationale,
    decimal? Confidence);

public record WrapUpUnparentedDto(
    Guid StorylineId, string Name, string Status, DateTimeOffset FirstDevelopmentAt);

public record WrapUpParentOptionDto(Guid StorylineId, string Name, string Status);

public record WrapUpApplyResult(int Closed, int Nested, int Rejected, int Parented, Guid? BatchId);

/// <summary>POST body for applying wrap-up decisions. All lists optional; empty is a no-op.</summary>
public record WrapUpDecisionsBody(
    IReadOnlyList<WrapUpClosureBody>? Closures,
    IReadOnlyList<Guid>? AcceptProposalIds,
    IReadOnlyList<Guid>? RejectProposalIds,
    IReadOnlyList<WrapUpParentBody>? Parents);

public record WrapUpClosureBody(Guid StorylineId, string Status);

public record WrapUpParentBody(Guid ChildStorylineId, Guid ParentStorylineId);

public record StorylineTimelineDto(
    IReadOnlyList<TimelineSessionDto> Sessions,
    IReadOnlyList<TimelineLaneDto> Lanes,
    IReadOnlyList<TimelineLinkDto> Links);

public record TimelineSessionDto(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    int StorylineCount);

public record TimelineLaneDto(
    Guid StorylineId,
    string Name,
    string Status,
    IReadOnlyList<TimelinePointDto> Points,
    Guid? ParentStorylineId = null,
    string? CampaignName = null,
    DateTimeOffset? CampaignStartedAt = null,
    IReadOnlyList<TimelineLaneCampaignDto>? Campaigns = null);

/// <summary>A campaign a storyline lane spans — declared by the GM, derived from sessions, or both.</summary>
public record TimelineLaneCampaignDto(
    Guid CampaignId,
    string Name,
    DateTimeOffset? StartedAt,
    bool Declared,
    bool Derived);

public record TimelinePointDto(
    Guid SourceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<TimelineDevelopmentDto> Developments,
    Guid? CampaignId = null);

public record TimelineDevelopmentDto(
    string Kind,
    string Text,
    string? Quote,
    bool IsOpenQuestion);

public record TimelineLinkDto(
    Guid FromStorylineId,
    Guid ToStorylineId,
    string Type);

public record LibraryDocumentDto(
    Guid Id,
    Guid WorldId,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility,
    string Status,
    int? PageCount,
    int ChunkCount,
    string? ErrorMessage,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record RequestLibraryUploadRequest(
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility);

public record LibraryUploadTicketDto(LibraryDocumentDto Document, string UploadUrl);

public record LibraryDownloadDto(string DownloadUrl, string FileName, string ContentType, long SizeBytes);

/// <summary>Public face of a world — the anonymous /w/{slug} pages' card.</summary>
public record PublicWorldDto(
    string Slug,
    string Name,
    string? Description,
    string? GameSystem);

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
