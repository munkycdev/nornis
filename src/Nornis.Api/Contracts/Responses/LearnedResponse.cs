namespace Nornis.Api.Contracts.Responses;

public record LearnedElementResponse(Guid Id, string Kind, string Name, string? Detail);

/// <summary>
/// <paramref name="GmNote"/> is the GM's own words where they wrote any — never the composed
/// source body, which also lists counts of what was promoted.
/// </summary>
public record LearnedEntryResponse(
    string Kind,
    Guid SourceId,
    DateTimeOffset OccurredAt,
    string? GmNote,
    IReadOnlyList<LearnedElementResponse> Elements);

/// <summary>
/// What this member has been told since they last looked. Carries no count or trace of anything
/// still withheld — <paramref name="HasMore"/> is a paging fact about disclosures this reader
/// may see, not a hint that something is hidden.
/// </summary>
public record LearnedResponse(
    Guid WorldId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? SeenThrough,
    bool HasMore,
    IReadOnlyList<LearnedEntryResponse> Entries);
