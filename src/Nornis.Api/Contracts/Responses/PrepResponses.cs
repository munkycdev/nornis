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
/// The draft stream's server-sent event names, which a side-conversation turn streams under too
/// (its <c>done</c> carries a <see cref="ConversationMessageResponse"/>). Mirrored by
/// <c>DraftStreamEventNames</c> in the Web, which shares no project with the API.
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

/// <summary>The booklet. <see cref="Html"/> is escaped, sanitized markup, ready to show as is.</summary>
public record AdventurePrintResponse(Guid AdventureId, string Title, string? Slug, string Html, string LibraryTitle);

/// <summary>The source an export filed, so the page can link to it and on to Review.</summary>
public record AdventureExportResponse(Guid SourceId, string Title, string? Slug);

/// <summary>One message of an Adventure's side conversation. <see cref="Role"/> is User (the GM) or Assistant.</summary>
public record ConversationMessageResponse(Guid Id, string Role, string Content, string? Model, DateTimeOffset CreatedAt);

/// <summary>The side conversation, oldest first. <see cref="IsFull"/> means it must be cleared before another turn.</summary>
public record ConversationResponse(IReadOnlyList<ConversationMessageResponse> Messages, bool IsFull);
