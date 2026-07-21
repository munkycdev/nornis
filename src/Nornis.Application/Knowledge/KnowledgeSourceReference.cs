namespace Nornis.Application.Knowledge;

public class KnowledgeSourceReference
{
    public required Guid Id { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
    public string? Quote { get; init; }
    public required string ReferenceId { get; init; }

    /// <summary>Title of the owning source, when loaded — lets the prompt attribute
    /// provenance ("recorded in ...").</summary>
    public string? SourceTitle { get; init; }

    /// <summary>When the owning source's events happened (OccurredAt ?? CreatedAt),
    /// when loaded — lets the prompt date-stamp retrieved knowledge.</summary>
    public DateTimeOffset? SourceDate { get; init; }
}
