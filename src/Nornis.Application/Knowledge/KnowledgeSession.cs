namespace Nornis.Application.Knowledge;

/// <summary>
/// A recent play session (a Source of a session-recording type) included in the
/// Loremaster's context so time-anchored questions ("what happened last session?")
/// ground in the actual most recent sessions rather than whatever facts happened
/// to be retrieved.
/// </summary>
public class KnowledgeSession
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }

    /// <summary>When the session was played: OccurredAt when recorded, CreatedAt otherwise.</summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>Session text: the typed Body when present, machine-derived text otherwise.</summary>
    public string? Text { get; init; }

    public required string ReferenceId { get; init; }
}
