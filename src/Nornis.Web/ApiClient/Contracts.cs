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
    bool IsTemplate = false,
    // The campaign this world is playing now; null when none is. What a capture defaults to.
    Guid? CurrentCampaignId = null,
    // Prep's monthly AI cap; null inherits the configured default.
    decimal? PrepMonthlyBudgetUsd = null);

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
    bool? IsTemplate = null,
    decimal? PrepMonthlyBudgetUsd = null,
    bool ClearPrepBudget = false);

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
    string? CampaignName = null,
    string? Slug = null, string? CampaignSlug = null,
    bool StuckInQueue = false);

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
    string? DerivedText = null,
    Guid? LibraryDocumentId = null,
    string? LibraryDocumentTitle = null,
    int? LibraryPageFrom = null,
    int? LibraryPageTo = null,
    string? Slug = null, string? CampaignSlug = null, string? LibraryDocumentSlug = null,
    Guid? AdventureId = null,
    bool StuckInQueue = false);

// Mirrors Nornis.Api LinkedLocationResponse: one Location a session is linked to.
public record LinkedLocationDto(Guid ArtifactId, string Name, string? Summary, string? Slug = null);

public record CreateSourceRequest(
    string Title,
    string Type,
    string Visibility,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt,
    Guid? CampaignId = null,
    bool ExtractionEnabled = true);

// Mirrors Nornis.Api TranscribeSourceResponse: the vision reading of a handwritten
// source's page images, already persisted as its body. Empty means the pages held no
// readable handwriting.
public record TranscribeSourceResponse(string Markdown);

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
    string? Quote,
    string? Slug = null);

public record SourceKnowledgeFactDto(
    Guid FactId,
    Guid ArtifactId,
    string ArtifactName,
    string Predicate,
    string Value,
    string TruthState,
    string Visibility,
    string? Quote,
    string? ArtifactSlug = null);

public record SourceKnowledgeRelationshipDto(
    Guid RelationshipId,
    Guid ArtifactAId,
    string ArtifactAName,
    string Type,
    Guid ArtifactBId,
    string ArtifactBName,
    string? Quote,
    string? ArtifactASlug = null, string? ArtifactBSlug = null);

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
    decimal? Confidence,
    string? ArtifactSlug = null);

public record MapViewDto(
    SourceAttachmentDto Attachment,
    string ImageUrl,
    IReadOnlyList<MapPlacemarkDto> Placemarks);

// Mirrors Nornis.Api JourneyResponse.
public record JourneyLocationDto(Guid ArtifactId, string Name, decimal X, decimal Y, string? Label, string? ArtifactSlug = null);

public record JourneyHighlightDto(Guid ArtifactId, string Name, string Type, bool FirstSeen, string? Summary, string? ArtifactSlug = null);

public record JourneyStopDto(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> VisitedLocationIds,
    IReadOnlyList<JourneyHighlightDto> Highlights,
    string? SourceSlug = null);

public record JourneyDto(
    Guid MapAttachmentId,
    Guid MapSourceId,
    string ImageUrl,
    IReadOnlyList<JourneyLocationDto> Locations,
    IReadOnlyList<JourneyStopDto> Stops,
    int UndatedSessionCount,
    string? MapSourceSlug = null);

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
    DateTimeOffset CreatedAt,
    string? CurrentSourceSlug = null);

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
    Guid CreatedByUserId,
    string? Slug = null);

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
    DateTimeOffset? EndedAt = null,
    bool ClearStartedAt = false,
    bool ClearEndedAt = false);

public record PlayerDto(
    Guid Id,
    Guid WorldId,
    string Name,
    Guid? WorldMemberId,
    string? Role);

public record CreatePlayerRequest(string Name);

public record RenamePlayerRequest(string Name);

public record LinkPlayerRequest(Guid WorldMemberId);

public record MoveCharacterRequest(Guid PlayerId);

/// <summary>A standing shelf link as the GM sees it; the code is the secret.</summary>
public record ShelfLinkDto(
    Guid Id,
    Guid PlayerId,
    string Code,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public record CreateShelfLinkRequest(Guid PlayerId);

/// <summary>What a shelf link opens: the party shelf, named for its world and player.</summary>
public record ShelfDto(
    string WorldName,
    string PlayerName,
    IReadOnlyList<ShelfDocumentDto> Documents);

public record ShelfDocumentDto(
    Guid Id,
    string Title,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? PageCount,
    bool InBibliotaph = false);

/// <summary>Where opening a book goes: Bibliotaph's reader (<c>reader</c>) or a download (<c>download</c>).</summary>
public record LibraryReadDto(string Url, string Kind);

public record LibraryReadRequest(int? StartPage);

/// <summary>One open of a Bibliotaph book, for the GM.</summary>
public record LibraryOpenDto(string ViewerName, DateTimeOffset OpenedAt, bool FromShelfLink);

public record CharacterDto(
    Guid Id,
    Guid WorldId,
    Guid PlayerId,
    string PlayerName,
    string Name,
    string? Description,
    Guid? ArtifactId,
    IReadOnlyList<Guid> CampaignIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SheetUpdatedAt = null,
    string? Slug = null, string? ArtifactSlug = null);

/// <param name="Record">
/// Null both when the character is unlinked and when the reader may not see the artifact it
/// links to. The UI must render those two cases identically; anything else discloses that a
/// GM-only artifact exists bearing this character's name.
/// </param>
public record CharacterDossierDto(
    CharacterDto Character,
    string PlayerName,
    IReadOnlyList<string> CampaignNames,
    CharacterRecordDto? Record,
    string? Sheet = null,
    bool SheetSharedWithParty = false,
    bool CanEditSheet = false,
    bool CanShareSheet = false,
    IReadOnlyList<CharacterSnapshotDto>? Snapshots = null,
    IReadOnlyList<UnreconciledItemDto>? UnreconciledItems = null);

public record UnreconciledItemDto(
    Guid ArtifactId,
    string Name,
    string Type,
    string? Slug = null);

public record CharacterSnapshotDto(
    Guid Id,
    Guid SourceId,
    string SourceTitle,
    DateTimeOffset AsOf,
    string? Note,
    string? SourceSlug = null);

public record AttachCharacterSnapshotRequest(Guid SourceId, DateTimeOffset AsOf, string? Note = null);

public record UpdateCharacterSheetRequest(string? Sheet);

public record SetCharacterSheetSharingRequest(bool SharedWithParty);

public record CharacterRecordDto(
    Guid ArtifactId,
    string ArtifactName,
    string? Summary,
    IReadOnlyList<ArtifactFactDto> Facts,
    int TotalFactCount,
    IReadOnlyList<CharacterRecordGroupDto> Groups,
    string? ArtifactSlug = null);

public record CharacterRecordGroupDto(
    string Type,
    IReadOnlyList<ConnectedArtifact> Artifacts,
    int TotalCount);

public record CreateCharacterRequest(
    string Name,
    string? Description = null,
    Guid? PlayerId = null,
    Guid? ArtifactId = null);

public record UpdateCharacterRequest(
    string? Name = null,
    string? Description = null,
    Guid? ArtifactId = null,
    bool UnlinkArtifact = false);

public record AssignCampaignCharactersRequest(
    IReadOnlyCollection<Guid> CharacterIds);

// Mirrors Nornis.Api ReorderCampaignsRequest.
public record ReorderCampaignsRequest(IReadOnlyList<Guid> CampaignIds);

// Mirrors Nornis.Api CampaignDetailResponse: everything the campaign page shows, already
// narrowed to what the caller may see.
public record CampaignDetailDto(
    CampaignDto Campaign,
    IReadOnlyList<CharacterDto> Characters,
    IReadOnlyList<CampaignArtifactDto> Artifacts,
    int ArtifactTotalCount,
    IReadOnlyList<SourceListItem> RecentSessions,
    int SessionCount,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    CampaignRecapDto Recap,
    UnfiledSourcesDto UnfiledInSpan);

// Mirrors Nornis.Api UnfiledSourcesResponse: sources under no campaign whose date falls
// inside this campaign's. Items is the first page of TotalCount.
public record UnfiledSourcesDto(
    IReadOnlyList<SourceListItem> Items,
    int TotalCount);

// Mirrors Nornis.Api FileCampaignSourcesRequest.
public record FileCampaignSourcesRequest(IReadOnlyList<Guid> SourceIds);

// Mirrors Nornis.Api FileCampaignSourcesResponse.
public record FileCampaignSourcesResponse(int FiledCount);

// Mirrors Nornis.Api CampaignArtifactResponse.
public record CampaignArtifactDto(
    Guid Id,
    string Name,
    string Type,
    string? Summary,
    string Status,
    int SourceCount,
    string? Slug = null);

// Mirrors Nornis.Api CampaignRecapResponse.
public record CampaignRecapDto(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview);

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
    DateTimeOffset UpdatedAt,
    string? Slug = null);

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
    string? Summary = null,
    string? Slug = null);

public record SourceReferenceDto(
    Guid Id,
    Guid SourceId,
    string TargetType,
    Guid TargetId,
    string? Quote,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? SourceTitle = null,
    string? SourceSlug = null);

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
    string? Slug = null);

/// <summary>A campaign a storyline is declared to belong to (id + name only).</summary>
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
    DateTimeOffset UpdatedAt,
    string? ArtifactSlug = null, string? OtherArtifactSlug = null);

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

/// <param name="PriceEstimated">The dollars are an estimate (a model Nornis hasn't tested ran).</param>
public record ModelCost(string Model, CostSummary Summary, bool PriceEstimated = false);

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
    Guid? DocumentId = null,
    string? Slug = null);

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
    bool IsStale,
    string? ArtifactSlug = null);

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
    bool Missing,
    string? ArtifactSlug = null);

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

public record GraphNode(Guid Id, string Name, string Type, string Status, string? Slug = null);

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

public record JumpDto(IReadOnlyList<JumpGroupDto> Groups);
public record JumpGroupDto(string Kind, IReadOnlyList<JumpItemDto> Items, int TotalCount);
public record JumpItemDto(Guid Id, string Name, string? Detail, string? Slug = null);

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
    int StorylineCount,
    string? SourceSlug = null);

public record TimelineLaneDto(
    Guid StorylineId,
    string Name,
    string Status,
    IReadOnlyList<TimelinePointDto> Points,
    Guid? ParentStorylineId = null,
    string? CampaignName = null,
    DateTimeOffset? CampaignStartedAt = null,
    IReadOnlyList<TimelineLaneCampaignDto>? Campaigns = null,
    string? Slug = null);

/// <summary>A campaign a storyline lane spans — declared by the GM, derived from sessions, or both.</summary>
public record TimelineLaneCampaignDto(
    Guid CampaignId,
    string Name,
    DateTimeOffset? StartedAt,
    string? Slug = null);

public record TimelinePointDto(
    Guid SourceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<TimelineDevelopmentDto> Developments,
    Guid? CampaignId = null,
    string? SourceSlug = null);

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
    DateTimeOffset UpdatedAt,
    string? Slug = null,
    Guid? AdventureId = null,
    string Origin = "Upload",
    bool InBibliotaph = false,
    bool HasCover = false,
    // Feature 36, decided by the API: Book, Map, Handout, Print or Other; the Adventure an upload
    // belongs to (GMs only); and whether it shows on the Shelf.
    string Category = "Other",
    Guid? LinkedAdventureId = null,
    bool OnShelf = true,
    // Feature 40: a print of an Adventure that drew on the web, so sharing it warns.
    bool ResearchedOnWeb = false)
{
    /// <summary>A book from the world's Bibliotaph: read there, never downloaded or deleted here.</summary>
    public bool IsFromBibliotaph => Origin == "Bibliotaph";

    /// <summary>An upload the GM may file under an Adventure or re-kind: not a print, not a Bibliotaph book.</summary>
    public bool IsOrganisable => !IsFromBibliotaph && Category != PrintCategory;

    public const string BookCategory = "Book";
    public const string MapCategory = "Map";
    public const string HandoutCategory = "Handout";
    public const string PrintCategory = "Print";
    public const string OtherCategory = "Other";

    /// <summary>The categories in chip order; mirrored from the Domain's <c>LibraryDocumentCategory</c>.</summary>
    public static readonly string[] Categories = [BookCategory, MapCategory, HandoutCategory, PrintCategory, OtherCategory];
}

/// <summary>The Library's Bibliotaph strip. <see cref="State"/> mirrors the API's shelf states.</summary>
public record LibraryBibliotaphDto(string State, string? OwnerName)
{
    public const string None = "none";
    public const string Ok = "ok";
    public const string NeedsReconnect = "needs_reconnect";
    public const string Opening = "opening";
    public const string NotSetUp = "not_set_up";
    public const string Unreachable = "unreachable";
}

/// <summary>
/// An ordinary upload. A Prep print is not uploaded: the print page files it in one click
/// (feature 34), and the API refuses an upload that names an Adventure.
/// </summary>
public record RequestLibraryUploadRequest(
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,
    string Visibility,
    // The Adventure this upload belongs to (feature 36); GM only.
    Guid? LinkedAdventureId = null);

public record LibraryUploadTicketDto(LibraryDocumentDto Document, string UploadUrl);

public record LibraryDownloadDto(string DownloadUrl, string FileName, string ContentType, long SizeBytes);

public record SearchLibraryExcerptsRequest(Guid? ArtifactId, string? Query);

/// <summary>Pages are 1-based. <paramref name="Pages"/> are ticked pages of a Bibliotaph book.</summary>
public record FileLibraryExcerptRequest(
    IReadOnlyList<Guid>? ChunkIds, int? PageFrom, int? PageTo, Guid? ArtifactId, IReadOnlyList<int>? Pages = null);

/// <summary>An indexed chunk (<paramref name="ChunkId"/> set) or a Bibliotaph page, filed by <paramref name="Page"/>.</summary>
public record LibraryExcerptCandidateDto(
    Guid? ChunkId, Guid DocumentId, string DocumentTitle, int Page, string Text, string? PageLabel = null)
{
    /// <summary>What the dialog ticks: the chunk, or the book's page.</summary>
    public string Key => ChunkId is { } id ? id.ToString() : $"{DocumentId}:{Page}";

    /// <summary>The page as the book prints it, when Bibliotaph knows.</summary>
    public string PageShown => string.IsNullOrWhiteSpace(PageLabel) ? Page.ToString(System.Globalization.CultureInfo.InvariantCulture) : PageLabel;
}

/// <summary>One page of a Bibliotaph book. <paramref name="Page"/> is 1-based.</summary>
public record LibraryPageDto(int Page, string? Label, string Text);

public record LibraryExcerptFiledDto(Guid SourceId, string Title, string ProcessingStatus, string? Slug = null);

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

/// <summary>A campaign as the public world shows it: the party recap, names, what it touched.</summary>
public record PublicCampaignDetailDto(
    CampaignDto Campaign,
    IReadOnlyList<PublicCampaignCastDto> Cast,
    IReadOnlyList<CampaignArtifactDto> Artifacts,
    int ArtifactTotalCount,
    IReadOnlyList<SourceListItem> RecentSessions,
    int SessionCount,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    PublicCampaignRecapDto Recap);

public record PublicCampaignCastDto(Guid Id, string Name, string PlayerName);

public record PublicCampaignRecapDto(bool HasData, DateTimeOffset? GeneratedAt, string? Content);

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

// ------------------------------------------------------------------------- Prep --

/// <summary>An Adventure on the Prep list. Mirrors the API's <c>AdventureSummaryResponse</c>.</summary>
public record AdventureSummaryDto(
    Guid Id,
    string Title,
    string? Slug,
    Guid? CampaignId,
    int SectionCount,
    int FilledSectionCount,
    Guid? LastPrintedDocumentId,
    DateTimeOffset UpdatedAt,
    bool IsExported);

public record AdventureSectionDto(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    int RevisionCount,
    bool IsFilled,
    // What the section's game blocks would lose on export (feature 35); the server decides them.
    IReadOnlyList<string>? BlockWarnings = null);

/// <summary>A section as a save leaves it; a save never changes its revisions.</summary>
public record SavedAdventureSectionDto(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    bool IsFilled,
    IReadOnlyList<string>? BlockWarnings = null);

public record AdventureDto(
    Guid Id,
    Guid WorldId,
    Guid? CampaignId,
    string Title,
    string? Slug,
    string? Pitch,
    Guid? LastPrintedDocumentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdventureSectionDto> Sections,
    AdventureDetailsDto? Details = null,
    PrintDetailsDto? PrintDetails = null,
    IReadOnlyList<string>? IgnoredDetails = null,
    AdventureDraftDto? Draft = null,
    string? TableBrief = null,
    IReadOnlyList<AdventureSourceBookDto>? SourceBooks = null,
    AdventureAiUseDto? AiUse = null,
    IReadOnlyList<AdventureHandoutDto>? Handouts = null);

/// <summary>
/// A handout the booklet prints at its back (feature 38), in print order. <see cref="Category"/> is
/// Map or Handout. Mirrors <c>AdventureHandoutResponse</c>.
/// </summary>
public record AdventureHandoutDto(Guid Id, string Title, string? Slug, string Category, string ContentType, string Visibility);

/// <summary>The booklet's handouts, the whole ordered list (feature 38): add, remove and reorder are all this.</summary>
public record SetHandoutsRequest(IReadOnlyList<Guid> DocumentIds);

/// <summary>
/// AI's part in the Adventure (feature 38). <see cref="Locked"/> means recorded use: the box is
/// ticked and can't be unticked. <see cref="Kind"/> is None, Declared, Text, Art or TextAndArt.
/// Mirrors <c>AdventureAiUseResponse</c>. <see cref="ResearchedOnWeb"/> (feature 40): a research
/// answer's text went into the Adventure.
/// </summary>
public record AdventureAiUseDto(bool WroteText, bool MadeArt, bool Declared, bool Locked, string Kind, bool ResearchedOnWeb = false);

/// <summary>The GM's own AI-use tick (feature 38); the API ignores it once AI use is recorded.</summary>
public record SetAiDeclaredRequest(bool Declared);

/// <summary>
/// The Cover card (feature 38). <see cref="Options"/> are generated options, newest first;
/// <see cref="GenerateBlocker"/> says why Generate can't run; <see cref="Warning"/> is an upload's
/// note that the art may print soft.
/// </summary>
public record CoverStateDto(
    CoverDto? Cover, string? Brief, IReadOnlyList<Guid> Options, Guid? ChosenOptionId,
    int ImagesGenerated, int ImagesCap, bool CanGenerate, string? GenerateBlocker, string? Warning);

/// <summary>Source is Generated or Uploaded; Layout is FullBleed, Framed or ClassicModule.</summary>
public record CoverDto(string Source, string Layout, string ContentType, DateTimeOffset UpdatedAt);

public record CoverBriefRequest(string? Brief);

public record ChooseCoverRequest(Guid OptionId);

public record SetCoverLayoutRequest(string? Layout);

/// <summary>
/// The design draft (feature 37): one Markdown document the GM and the Loremaster iterate on,
/// never printed and never exported. Saves are based on <see cref="UpdatedAt"/>. Mirrors
/// <c>AdventureDraftResponse</c>.
/// </summary>
public record AdventureDraftDto(string Markdown, DateTimeOffset UpdatedAt, int RevisionCount);

/// <summary>A Library book the Loremaster reads for this adventure (feature 37). Mirrors <c>AdventureSourceBookResponse</c>.</summary>
public record AdventureSourceBookDto(Guid Id, string Title, string? Slug);

/// <summary>Saves the design draft, based on the stamp it was opened at (null: there was none).</summary>
public record SaveAdventureDraftRequest(string Markdown, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>The table brief; blank clears it.</summary>
public record SetTableBriefRequest(string? Brief);

/// <summary>The Adventure's source books, exactly; empty clears the choice.</summary>
public record SetSourceBooksRequest(IReadOnlyList<Guid> DocumentIds);

/// <summary>The four details the GM edits (feature 32). A null game system means the world's. Mirrors <c>AdventureDetailsResponse</c>.</summary>
public record AdventureDetailsDto(string? GameSystem, string? Edition, string? Levels, int? Year);

/// <summary>
/// What a print filed now would carry, defaults applied and fixed values filled in by the API
/// (feature 32). The Book details card shows it as is. Mirrors <c>PrintDetailsResponse</c>.
/// </summary>
public record PrintDetailsDto(
    string Title, string? GameSystem, string? Edition, string Type, string? Levels,
    string Publisher, string Author, int? Year, string? AiUse = null);

/// <summary>All four editable details: each is the new value, and null clears it.</summary>
public record UpdateAdventureDetailsRequest(string? GameSystem, string? Edition, string? Levels, int? Year);

public record CreateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

public record ImportAdventureFile(string Name, string Markdown);

public record ImportAdventureRequest(IReadOnlyList<ImportAdventureFile> Files, Guid? CampaignId);

/// <summary>Every field is the new value: a null pitch means none, a null campaign means world-level.</summary>
public record UpdateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

public record SaveAdventureSectionRequest(string Kind, string Title, string? BodyMarkdown, DateTimeOffset BasedOnUpdatedAt);

/// <summary>Draft or revise one section. A null or blank instruction is a plain Draft or Revise.</summary>
public record DraftAdventureSectionRequest(string? Instruction, DateTimeOffset BasedOnUpdatedAt);

/// <summary>This month's Prep spend. A null ceiling means none is set.</summary>
public record PrepBudgetDto(decimal SpentThisMonthUsd, decimal? MonthlyBudgetUsd, bool IsExceeded);

public record AddAdventureSectionRequest(string Kind, string Title, Guid? AfterSectionId);

public record ReorderAdventureSectionsRequest(IReadOnlyList<Guid> SectionIds);

/// <summary>Export to Nornis. A null chapter is the whole Adventure.</summary>
public record ExportAdventureRequest(Guid? ChapterSectionId);

/// <summary>The source an export filed.</summary>
public record AdventureExportDto(Guid SourceId, string Title, string? Slug);

/// <summary>One message of an Adventure's side conversation. Mirrors the API's <c>ConversationMessageResponse</c>.</summary>
/// <param name="Edits">The suggested edits to the design draft the answer carries (feature 37), read by the API; null when none.</param>
/// <param name="Prose">The answer without its edit fences, to show around the edit cards; null when it has none.</param>
/// <param name="WebResearch">What a research answer found on the web (feature 40); null when the turn wasn't one.</param>
public record ConversationMessageDto(
    Guid Id, string Role, string Content, string? Model, DateTimeOffset CreatedAt,
    IReadOnlyList<DraftEditDto>? Edits = null, string? Prose = null, WebResearchDto? WebResearch = null)
{
    /// <summary>The role names the API sends, the domain's <c>ConversationRole</c>; pinned by a test.</summary>
    public const string UserRole = "User";
    public const string AssistantRole = "Assistant";

    public bool IsAnswer => Role == AssistantRole;
}

/// <summary>A research answer's web pages in the provider's order, and Gemini's search suggestions (feature 40). Mirrors <c>WebResearchResponse</c>.</summary>
public record WebResearchDto(IReadOnlyList<WebPageDto> Pages, int? Searches, string? SuggestionsHtml);

public record WebPageDto(string Title, string Url);

/// <summary>Whether the Web research switch can be turned on, and why not (feature 40). Mirrors <c>WebResearchAvailabilityResponse</c>.</summary>
public record WebResearchAvailabilityDto(bool Available, string? Reason);

/// <summary>The side conversation, oldest first. <see cref="IsFull"/> means it must be cleared first.</summary>
/// <param name="WebResearch">Whether the switch can be on (feature 40); null from an API before 3.11, read as off.</param>
public record ConversationDto(IReadOnlyList<ConversationMessageDto> Messages, bool IsFull, WebResearchAvailabilityDto? WebResearch = null);

/// <param name="Scope">Section, or Draft in Design mode (feature 37); the API's <c>ConversationScope</c>, pinned by a test.</param>
/// <param name="WebResearch">The Web research switch (feature 40): this turn may search the web.</param>
public record SendConversationMessageRequest(
    string Text, Guid? SectionId, string Scope = SendConversationMessageRequest.SectionScope, bool WebResearch = false)
{
    public const string SectionScope = "Section";
    public const string DraftScope = "Draft";
}

/// <summary>One suggested edit: the passage it finds in the draft and its replacement. Mirrors <c>DraftEditResponse</c>.</summary>
public record DraftEditDto(string Find, string Replace);

/// <summary>Applies an answer's <paramref name="Index"/>th edit, based on the draft's stamp.</summary>
public record ApplyDraftEditRequest(Guid MessageId, int Index, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>Puts a whole answer into the draft: <see cref="PutInSectionRequest.Replace"/> or <see cref="PutInSectionRequest.Append"/>.</summary>
public record PutInDraftRequest(Guid MessageId, string Mode, DateTimeOffset? BasedOnUpdatedAt);

public record PlanDraftRequest(bool UseLoremaster);

/// <summary>A split plan (feature 37). Mirrors <c>DraftPlanResponse</c>; Source is Headings or Loremaster.</summary>
public record DraftPlanDto(IReadOnlyList<DraftPlanRowDto> Rows, string Source, string? Note, DateTimeOffset? DraftUpdatedAt);

public record DraftPlanRowDto(int StartLine, string Title, string Kind, string Opening);

public record DraftSplitPointRequest(int StartLine, string Title, string Kind);

/// <summary>Cuts the draft into sections. Mode is <see cref="Replace"/> or <see cref="Append"/>, the API's <c>DraftSplitMode</c>.</summary>
public record SplitDraftRequest(IReadOnlyList<DraftSplitPointRequest> Plan, string Mode, DateTimeOffset? BasedOnUpdatedAt)
{
    public const string Replace = "Replace";
    public const string Append = "Append";
}

public record GatherDraftRequest(DateTimeOffset? BasedOnUpdatedAt);

/// <summary>What the Put in section dialog chose: the section, and <see cref="PutInSectionRequest.Replace"/> or <see cref="PutInSectionRequest.Append"/>.</summary>
public record PutInSectionChoice(Guid SectionId, string Mode);

/// <summary>Put one saved answer into a section. <see cref="Mode"/> is one of the constants below.</summary>
public record PutInSectionRequest(Guid MessageId, string Mode, DateTimeOffset BasedOnUpdatedAt)
{
    /// <summary>The API's <c>PutInSectionMode</c> names; pinned by a test.</summary>
    public const string Replace = "Replace";
    public const string Append = "Append";
}

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

public record LearnedElementDto(Guid Id, string Kind, string Name, string? Detail, string? Slug = null);

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

// ---------------------------------------------------------------- sign-ins (feature 33) --

/// <summary>One way of signing in, on Profile. <see cref="Current"/> is the one this session uses.</summary>
public record SignInDto(Guid Id, string Provider, DateTimeOffset AddedAt, bool Current);

public record SignInLinkDto(string Code);

/// <summary><see cref="Outcome"/> is <c>linked</c> or <c>already-linked</c>.</summary>
public record SignInClaimDto(string Outcome);

public record ClaimSignInBody(string Code);

// ------------------------------------------------------------- bibliotaph (feature 31) --

/// <summary>Profile › Bibliotaph. <see cref="Status"/> is one of the constants below.</summary>
public record BibliotaphConnectionDto(bool Configured, string Status, DateTimeOffset? ConnectedAt, DateTimeOffset? LastUsedAt)
{
    public const string None = "none";
    public const string Connected = "connected";
    public const string NeedsReconnect = "needs_reconnect";
}

/// <summary>An address the API wants the browser sent to.</summary>
public record BibliotaphRedirectDto(string Url);

public record BibliotaphAuthorizeRequest(string State);

// ---------------------------------------------------------------- AI keys (feature 39) --

/// <summary>One of the user's AI keys on Profile. The key itself never reaches the Web; the last
/// four characters say which one it is.</summary>
public record AiKeyDto(
    string Provider,
    string Label,
    string LastFour,
    string? BaseUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CheckedAt,
    bool CheckFailed,
    IReadOnlyList<AiCompatibleModelDto> Models);

/// <summary>One task's model on an OpenAI-compatible key; prices are USD per million tokens.</summary>
public record AiCompatibleModelDto(
    string Task,
    string ModelName,
    bool SupportsImages,
    decimal InputPerMillionUsd,
    decimal OutputPerMillionUsd);

/// <summary><see cref="Key"/> null keeps the stored key and checks it again.</summary>
public record SaveAiKeyBody(string? Key, string? Label, string? BaseUrl, IReadOnlyList<AiCompatibleModelDto>? Models);

public record SaveAiKeyResultDto(AiKeyDto Key, int SourcesQueued);

public record AiTaskModelsDto(string Task, IReadOnlyList<AiModelOptionDto> Options, string? ChosenId, string? DefaultId);

/// <param name="Tested">False for an OpenAI-compatible model, and for one the key's provider lists
/// beyond the catalog ("More from your key"), which Nornis has never run.</param>
public record AiModelOptionDto(string Id, string Name, string Provider, bool SupportsImages, bool HasKey, bool Tested);

public record ChooseAiModelBody(string? ModelId);

/// <param name="InUse">Whether Nornis pays through trials at all yet.</param>
public record AiTrialDto(bool InUse, decimal AllowanceUsd, decimal SpentUsd, decimal RemainingUsd, bool Available);

/// <summary>A world's AI funding, for its GMs: whether some of its AI work runs on the creator's trial.</summary>
public record WorldAiFundingDto(bool Trial, decimal? TrialRemainingUsd, bool CreatorIsYou);

/// <summary>What one payer covered: <see cref="PaidBy"/> is a key's label, "Trial" or "Nornis".</summary>
public record FundingCost(string Kind, string PaidBy, CostSummary Summary);
