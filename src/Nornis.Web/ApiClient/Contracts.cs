namespace Nornis.Web.ApiClient;

// Client-owned mirrors of the nornis-api JSON contracts. The Web is a separate deployable,
// so it owns its view of the wire shape rather than referencing the API's types. Enum-valued
// fields are carried as strings, exactly as the API serializes them.

public record CampaignSummary(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    string MyRole);

public record CreateCampaignRequest(
    string Name,
    string? Description,
    string? GameSystem);

public record UpdateCampaignRequest(
    string Name,
    string? Description,
    string? GameSystem);

public record CampaignMember(
    Guid Id,
    Guid CampaignId,
    Guid UserId,
    string Role,
    string? DisplayName,
    string? CharacterName,
    DateTimeOffset JoinedAt);

public record AddMemberRequest(
    Guid UserId,
    string Role);

public record UpdateMemberRoleRequest(
    string Role);

public record SourceListItem(
    Guid Id,
    Guid CampaignId,
    string Type,
    string Title,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus);

public record SourceDetail(
    Guid Id,
    Guid CampaignId,
    string Type,
    string Title,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus);

public record CreateSourceRequest(
    string Title,
    string Type,
    string Visibility,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt);

public record ArtifactListItem(
    Guid Id,
    Guid CampaignId,
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
    DateTimeOffset CreatedAt);

public record ArtifactDetailDto(
    Guid Id,
    Guid CampaignId,
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
    DateTimeOffset CreatedAt);

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
