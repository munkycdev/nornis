namespace Nornis.Api.Contracts.Responses;

/// <summary>A campaign backlog import and its notes, with each note's derived state.</summary>
public record ImportSessionResponse(
    Guid Id,
    Guid WorldId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ImportSessionItemResponse> Items,
    Guid? CurrentItemId,
    int? CurrentIndex,
    int SettledCount);

/// <param name="CreatedByImport">This note was typed into the import, so it may be deleted
/// along with its item. False for a source that existed beforehand.</param>
/// <param name="ExistingReferenceCount">How much canon this source has already contributed —
/// zero means re-running it is a first extraction, not a re-extraction.</param>
public record ImportSessionItemResponse(
    Guid Id,
    Guid SourceId,
    int Position,
    bool Skipped,
    string Title,
    DateTimeOffset? OccurredAt,
    string ProcessingStatus,
    string State,
    int OpenProposalCount,
    bool CreatedByImport,
    int ExistingReferenceCount);

/// <summary>A source that could be staged into the run.</summary>
public record ImportCandidateResponse(
    Guid SourceId,
    string Title,
    string Type,
    DateTimeOffset StoryPosition,
    bool IsDated,
    string ProcessingStatus,
    int ExistingReferenceCount,
    bool AlreadyStaged);
