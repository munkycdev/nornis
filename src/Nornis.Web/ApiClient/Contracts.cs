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
    bool PublicAccessEnabled = false,
    decimal? PublicAskMonthlyBudgetUsd = null,
    bool SummaryReviewRequired = false,
    bool IsDemo = false,
    bool TutorialEnabled = false,
    bool IsTemplate = false);

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
    bool? PublicAccessEnabled = null,
    decimal? PublicAskMonthlyBudgetUsd = null,
    bool ClearPublicAskBudget = false,
    bool? SummaryReviewRequired = null,
    bool? IsTemplate = null);

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
    bool ClearBody = false,
    string? Uri = null,
    bool ClearUri = false,
    DateTimeOffset? OccurredAt = null,
    bool ClearOccurredAt = false,
    string? Type = null,
    string? Visibility = null,
    Guid? CampaignId = null,
    bool ClearCampaign = false,
    bool? ExtractionEnabled = null);

// Mirrors Nornis.Api SourceKnowledgeResponse: what a source's extraction contributed
// to the record, limited to what the reader may see.
public record SourceKnowledgeDto(
    IReadOnlyList<SourceKnowledgeArtifactDto> Artifacts,
    IReadOnlyList<SourceKnowledgeFactDto> Facts,
    IReadOnlyList<SourceKnowledgeRelationshipDto> Relationships);

public record SourceKnowledgeArtifactDto(
    Guid ArtifactId,
    string Name,
    string Type,
    string? Quote);

public record SourceKnowledgeFactDto(
    Guid FactId,
    Guid ArtifactId,
    string ArtifactName,
    string Predicate,
    string Value,
    string TruthState,
    string Visibility,
    string? Quote);

public record SourceKnowledgeRelationshipDto(
    Guid RelationshipId,
    Guid ArtifactAId,
    string ArtifactAName,
    string Type,
    Guid ArtifactBId,
    string ArtifactBName,
    string? Quote);

// Mirrors Nornis.Api RemoveFactRequest.
public record RemoveFactRequest(string Note);

// Mirrors Nornis.Api ReprocessSourceRequest: edits applied atomically with the
// reprocess; null fields keep current values.
public record ReprocessSourceRequest(
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    bool ClearOccurredAt = false);

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
    Guid MapSourceId,
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

// Mirrors Nornis.Api ExtractionReplayResponse: the world's timeline replay walk.
public record ExtractionReplayDto(
    Guid Id,
    string Status,
    Guid CurrentSourceId,
    string? CurrentSourceTitle,
    string? CurrentSourceProcessingStatus,
    int RemainingCount,
    DateTimeOffset CreatedAt);

// Mirrors Nornis.Api ExtractionReplayStateResponse: Replay is null when none is running.
public record ExtractionReplayStateDto(ExtractionReplayDto? Replay);

// Mirrors Nornis.Api ExtractionReplayPreviewResponse.
public record ExtractionReplayPreviewDto(int TotalSources);

// Mirrors Nornis.Api StartExtractionReplayRequest.
public record StartExtractionReplayRequest(Guid StartSourceId);

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
    Guid? CreatedEntityId,
    // Accept only: the Create bound to an artifact already in canon rather than inserting
    // one, so the feedback should say "matched", not "created".
    bool MatchedExistingArtifact = false,
    // Accept only: artifacts the accept had to create because the proposal named them and
    // nothing held them. The reviewer hears about canon growing beyond the card they clicked.
    IReadOnlyList<string>? CreatedMissingArtifactNames = null);

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
/// <see cref="EffectiveScore"/> reflects only the findings still Open and not stale. When the
/// world has never been assessed, <see cref="HasData"/> is false.
/// </summary>
public record ContinuityAssessment(
    bool HasData,
    Guid? AssessmentId,
    DateTimeOffset? CreatedAt,
    string? Model,
    int Score,
    int EffectiveScore,
    int HeuristicScore,
    IReadOnlyList<ContinuityFinding> Findings,
    ContinuityPenaltyBreakdown Penalty);

/// <summary>
/// The penalty arithmetic, done by the API. World Memory used to recompute this here — its own
/// severity table, its own cap, its own copy of the stale-suspension rule — and nothing spanning
/// the two deployables would have caught them drifting apart.
/// </summary>
public record ContinuityPenaltyBreakdown(
    IReadOnlyList<ContinuityPenaltyLine> Lines,
    IReadOnlyList<ContinuitySeverityWeight> Scale,
    int StaleSuspendedCount,
    int RawPenalty,
    int CappedPenalty,
    int Cap,
    bool IsCapped)
{
    /// <summary>Null-guard only. Every rendered path has a real breakdown from the API, including
    /// the never-assessed case — which still carries the scale.</summary>
    public static ContinuityPenaltyBreakdown Empty { get; } = new([], [], 0, 0, 0, 0, false);
}

public record ContinuityPenaltyLine(string Severity, int PenaltyEach, int Count, int Subtotal);

public record ContinuitySeverityWeight(string Severity, int PenaltyEach);

public record ContinuityFinding(
    Guid Id,
    string Category,
    string Severity,
    string Summary,
    string? SuggestedAction,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<ContinuityEvidenceItem> EvidenceItems,
    Guid? ArtifactId,
    string Status,
    bool IsStale);

/// <summary>
/// A cited evidence ref resolved for display. Changed items were edited after the assessment
/// ran (the finding is stale); missing ones no longer exist in the record.
/// </summary>
public record ContinuityEvidenceItem(
    string RefId,
    string Kind,
    string Label,
    Guid? ArtifactId,
    bool ChangedSinceAudit,
    bool Missing);

/// <summary>
/// The maintained world digest, rendered by the API for the caller's role: GMs get
/// <see cref="Content"/> = the GM digest plus <see cref="PartyPreview"/> = the party recap;
/// everyone else gets <see cref="Content"/> = the party recap and no preview. When the world
/// has never been digested, <see cref="HasData"/> is false.
/// </summary>
public record WorldDigest(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview);

/// <summary>
/// Result of drafting a fix for a finding: 0 proposals means the fixer had nothing concrete
/// to propose and no review batch was created.
/// </summary>
public record DraftFixResult(
    Guid? BatchId,
    Guid? SourceId,
    int ProposalCount);

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
    bool PendingProposalsCapped,
    int UnseenDisclosures)
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

public record ExportWorldRequest(IReadOnlyList<string> Categories);

/// <summary>A finished world export: a short-lived SAS download URL for the zip.</summary>
public record WorldExportDto(string DownloadUrl, string FileName, long SizeBytes);

/// <summary>Public face of a world — the anonymous /w/{slug} pages' card. <see cref="AskEnabled"/>
/// tells the site whether to offer anonymous "Ask the Loremaster".</summary>
public record PublicWorldDto(
    string Slug,
    string Name,
    string? Description,
    string? GameSystem,
    bool AskEnabled = false);

/// <summary>A single-shot anonymous ask against a public world.</summary>
public record PublicAskRequest(string Question);

// -------------------------------------------------- Onboarding + tutorial (feature 20) --

public record OnboardingStateDto(bool PromptSeen, bool TutorialDismissed);

public record TutorialStepDto(string Key, int Chapter, bool ClientReported, DateTimeOffset? CompletedAt);

public record TutorialChecklistDto(IReadOnlyList<TutorialStepDto> Steps);

public record TutorialSessionSixDto(string Body);

// -------------------------------------------------- Campaign backlog import (phase 2) --

// Mirrors Nornis.Api ImportSessionResponse: the backlog and where the walk stands.
public record ImportSessionDto(
    Guid Id,
    Guid WorldId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ImportSessionItemDto> Items,
    Guid? CurrentItemId,
    int? CurrentIndex,
    int SettledCount);

// Mirrors Nornis.Api ImportSessionItemResponse. State is derived server-side from the
// note's source: Waiting, Extracting, Reviewing, Failed, Done, Skipped.
public record ImportSessionItemDto(
    Guid Id,
    Guid SourceId,
    int Position,
    bool Skipped,
    string Title,
    DateTimeOffset? OccurredAt,
    string ProcessingStatus,
    string State,
    int OpenProposalCount,
    bool CreatedByImport = true,
    int ExistingReferenceCount = 0);

// Mirrors Nornis.Api ImportCandidateResponse — a source that could be staged into the run.
public record ImportCandidateDto(
    Guid SourceId,
    string Title,
    string Type,
    DateTimeOffset StoryPosition,
    bool IsDated,
    string ProcessingStatus,
    int ExistingReferenceCount,
    bool AlreadyStaged);

// Mirrors Nornis.Api AddImportNoteRequest.
public record AddImportNoteRequest(string Title, string? Body = null, DateTimeOffset? OccurredAt = null);

// Mirrors Nornis.Api AddExistingSourcesRequest.
public record AddExistingSourcesRequest(IReadOnlyList<Guid> SourceIds);

// Mirrors Nornis.Api ReorderImportItemsRequest.
public record ReorderImportItemsRequest(IReadOnlyList<Guid> ItemIds);

// Mirrors Nornis.Api AdvanceImportSessionRequest. ExpectedItemId is the item the screen was
// showing as current; the API refuses the call if the walk has moved on since.
public record AdvanceImportSessionRequest(bool SkipCurrent = false, Guid? ExpectedItemId = null);

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

// ------------------------------------------------------------- convergence gauge --

/// <summary>
/// The observations behind a candidate's score, and what they normalized to. The page renders
/// phrases from the counts and never recomputes a component — the score arrives decided.
/// </summary>
public record ConvergenceComponentsDto(
    int DaysHidden,
    int PartyVisibleFactsOnAnchor,
    int MissingArtifactCount,
    bool IsSelfContained,
    string? StorylineStatus,
    string? ContradictionSeverity,
    bool ContradictionAssessed,
    double Dormancy,
    double AnchorFamiliarity,
    double SelfContainment,
    double StorylineState,
    double? ContradictionPressure);

public record ConvergenceCandidateDto(
    string Kind,
    Guid Id,
    Guid AnchorArtifactId,
    string AnchorName,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> MissingArtifactIds,
    ConvergenceComponentsDto Components,
    int Score,
    string? Rationale);

/// <summary><c>AssessmentId</c> is null when the world has never been assessed.</summary>
public record ConvergenceDto(
    Guid WorldId,
    DateTimeOffset GeneratedAt,
    Guid? AssessmentId,
    int TotalCandidates,
    IReadOnlyList<ConvergenceCandidateDto> Candidates);

// ------------------------------------------------------------ what you learned --

public record LearnedElementDto(Guid Id, string Kind, string Name, string? Detail);

/// <summary><c>GmNote</c> is the GM's own words, never the composed source body.</summary>
public record LearnedEntryDto(
    string Kind,
    Guid SourceId,
    DateTimeOffset OccurredAt,
    string? GmNote,
    IReadOnlyList<LearnedElementDto> Elements);

/// <summary>
/// <c>HasMore</c> is a paging fact about disclosures this reader may see — never a hint that
/// anything is hidden.
/// </summary>
public record LearnedDto(
    Guid WorldId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? SeenThrough,
    bool HasMore,
    IReadOnlyList<LearnedEntryDto> Entries);

public record MarkLearnedSeenBody(DateTimeOffset SeenThrough);
