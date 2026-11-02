namespace Nornis.Api.Contracts.Responses;

public record AdventureSummaryResponse(
    Guid Id,
    string Title,
    string? Slug,
    Guid? CampaignId,
    int SectionCount,
    int FilledSectionCount,
    Guid? LastPrintedDocumentId,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One section. <see cref="IsFilled"/> is decided here, by <c>AdventureSkeleton.IsUnfilled</c>,
/// so the Web's outline and the list's count can't disagree about what an untouched scene is.
/// </summary>
public record AdventureSectionResponse(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    int RevisionCount,
    bool IsFilled);

/// <summary>A section as a save leaves it. A save never changes revisions, so their count isn't here.</summary>
public record SavedAdventureSectionResponse(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    bool IsFilled);

public record AdventureResponse(
    Guid Id,
    Guid WorldId,
    Guid? CampaignId,
    string Title,
    string? Slug,
    string? Pitch,
    Guid? LastPrintedDocumentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdventureSectionResponse> Sections);

/// <summary>
/// The draft stream's server-sent event names. Mirrored by <c>DraftStreamEventNames</c> in the
/// Web, which shares no project with the API.
/// </summary>
public static class DraftStreamEventNames
{
    /// <summary><see cref="DraftStreamDeltaResponse"/>: the next piece of the draft.</summary>
    public const string Delta = "delta";

    /// <summary><see cref="AdventureSectionResponse"/>: the section as the draft left it. The stream ends here.</summary>
    public const string Done = "done";

    /// <summary><see cref="ErrorResponse"/>: nothing was saved. The stream ends here.</summary>
    public const string Error = "error";
}

public record DraftStreamDeltaResponse(string Text);

public record PrepBudgetResponse(decimal SpentThisMonthUsd, decimal? MonthlyBudgetUsd, bool IsExceeded);
