namespace Nornis.Application.Models;

/// <summary>One thing the party can now see, resolved at the time of reading.</summary>
public sealed record LearnedElement(Guid Id, string Kind, string Name, string? Detail);

/// <summary>
/// One disclosure. <see cref="GmNote"/> is the GM's own words where they wrote any — never the
/// composed source body, which also lists counts of what was promoted and would disagree with
/// <see cref="Elements"/> once anything is archived.
/// </summary>
public sealed record LearnedEntry
{
    public required Guid SourceId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string? GmNote { get; init; }
    public required IReadOnlyList<LearnedElement> Elements { get; init; }
}

/// <summary>
/// What a member has been told since they last looked.
///
/// Carries no count, total, or trace of anything still withheld — a reader must not be able to
/// tell a world with nothing left to disclose from one with a hundred secrets in it.
/// <see cref="HasMore"/> is a paging fact about disclosures this reader may see, not a hint
/// that something is hidden.
/// </summary>
public sealed record LearnedDigest
{
    public required Guid WorldId { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>The marker as it stood when this was read; null if they had never looked.</summary>
    public required DateTimeOffset? SeenThrough { get; init; }

    /// <summary>Newest first.</summary>
    public required IReadOnlyList<LearnedEntry> Entries { get; init; }

    public required bool HasMore { get; init; }
}
