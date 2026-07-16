namespace Nornis.Application.Messaging;

/// <summary>
/// What a message on the source-extraction queue asks the worker to do. Old messages
/// serialized without a Kind deserialize to the default, <see cref="Extraction"/>.
/// </summary>
public enum ExtractionKind
{
    Extraction,
    RelationshipBackfill
}

public record ExtractionMessage(Guid SourceId, Guid WorldId, ExtractionKind Kind = ExtractionKind.Extraction);
