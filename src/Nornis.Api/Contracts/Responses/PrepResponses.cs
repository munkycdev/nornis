namespace Nornis.Api.Contracts.Responses;

public record AdventureSummaryResponse(
    Guid Id,
    string Title,
    string? Slug,
    Guid? CampaignId,
    int SectionCount,
    int FilledSectionCount,
    Guid? LastPrintedDocumentId,
    DateTimeOffset UpdatedAt,
    bool IsExported);

/// <summary>
/// One section. <see cref="IsFilled"/> is decided here, by <c>AdventureSkeleton.IsUnfilled</c>,
/// so the Web's outline and the list's count can't disagree about what an untouched scene is.
/// <see cref="BlockWarnings"/> come from <c>GameBlockWarnings</c> (feature 35), on every path that returns a section.
/// </summary>
public record AdventureSectionResponse(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    int RevisionCount,
    bool IsFilled,
    IReadOnlyList<string> BlockWarnings);

/// <summary>A section as a save leaves it. A save never changes revisions, so their count isn't here.</summary>
public record SavedAdventureSectionResponse(
    Guid Id,
    string Kind,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset UpdatedAt,
    bool IsFilled,
    IReadOnlyList<string> BlockWarnings);

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
    IReadOnlyList<AdventureSectionResponse> Sections,
    AdventureDetailsResponse Details,
    PrintDetailsResponse PrintDetails,
    IReadOnlyList<string>? IgnoredDetails = null,
    // Design mode (feature 37): the draft (null when there is none), the table brief and the
    // Library books the Adventure reads.
    AdventureDraftResponse? Draft = null,
    string? TableBrief = null,
    IReadOnlyList<AdventureSourceBookResponse>? SourceBooks = null,
    // Feature 38: what the Adventure records about AI's part in it.
    AdventureAiUseResponse? AiUse = null);

/// <summary>
/// AI's part in the Adventure (feature 38). <see cref="Locked"/> means recorded use: the box is
/// ticked and the GM can't untick it. <see cref="Kind"/> is what a print would say:
/// None, Declared, Text, Art or TextAndArt.
/// </summary>
public record AdventureAiUseResponse(bool WroteText, bool MadeArt, bool Declared, bool Locked, string Kind);

/// <summary>The design draft (feature 37); <see cref="RevisionCount"/> is how many changes Undo can step back.</summary>
public record AdventureDraftResponse(string Markdown, DateTimeOffset UpdatedAt, int RevisionCount);

/// <summary>A Library book the Adventure reads (feature 37).</summary>
public record AdventureSourceBookResponse(Guid Id, string Title, string? Slug);

/// <summary>The four details the GM edits (feature 32). A null game system means the world's.</summary>
public record AdventureDetailsResponse(string? GameSystem, string? Edition, string? Levels, int? Year);

/// <summary>
/// The details a print carries, defaults applied and fixed values filled in (feature 32). On an
/// Adventure, the preview of a print filed now; the Details card shows it as is.
/// </summary>
/// <param name="AiUse">What the print says about AI (feature 38): None, Declared, Text, Art or TextAndArt.</param>
public record PrintDetailsResponse(
    string Title, string? GameSystem, string? Edition, string Type, string? Levels,
    string Publisher, string Author, int? Year, string? AiUse = null);

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

/// <summary>The source an export filed, so the page can link to it and on to Review.</summary>
public record AdventureExportResponse(Guid SourceId, string Title, string? Slug);

/// <summary>One message of an Adventure's side conversation. <see cref="Role"/> is User (the GM) or Assistant.</summary>
/// <param name="Edits">The suggested edits to the design draft an answer carries (feature 37), in order; null when none.</param>
/// <param name="Prose">The answer with its edit fences taken out, to show around the edits; null when it has none.</param>
public record ConversationMessageResponse(
    Guid Id, string Role, string Content, string? Model, DateTimeOffset CreatedAt,
    IReadOnlyList<DraftEditResponse>? Edits = null, string? Prose = null);

/// <summary>A split plan (feature 37): Source is Headings or Loremaster; Note says why it isn't what was asked for.</summary>
public record DraftPlanResponse(IReadOnlyList<DraftPlanRowResponse> Rows, string Source, string? Note, DateTimeOffset? DraftUpdatedAt);

public record DraftPlanRowResponse(int StartLine, string Title, string Kind, string Opening);

/// <summary>One suggested edit: the passage it finds in the draft and what replaces it.</summary>
public record DraftEditResponse(string Find, string Replace);

/// <summary>The side conversation, oldest first. <see cref="IsFull"/> means it must be cleared before another turn.</summary>
public record ConversationResponse(IReadOnlyList<ConversationMessageResponse> Messages, bool IsFull);
