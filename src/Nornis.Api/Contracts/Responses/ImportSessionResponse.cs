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

public record ImportSessionItemResponse(
    Guid Id,
    Guid SourceId,
    int Position,
    bool Skipped,
    string Title,
    DateTimeOffset? OccurredAt,
    string ProcessingStatus,
    string State,
    int OpenProposalCount);
