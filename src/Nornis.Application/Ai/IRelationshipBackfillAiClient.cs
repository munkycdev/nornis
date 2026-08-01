namespace Nornis.Application.Ai;

/// <summary>
/// AI client for the storyline relationship backfill sweep: given one processed source
/// and the world's existing Storylines and Events, it proposes only "Advances"
/// (Event→Storyline) and "PartOf" (Storyline→Storyline) links.
/// </summary>
public interface IRelationshipBackfillAiClient
{
    Task<RelationshipBackfillAiResponse> ProposeLinksAsync(AiPromptRequest request, CancellationToken ct);
}

public class RelationshipBackfillAiResponse
{
    public required IReadOnlyList<BackfillLinkProposal> Links { get; init; }
    public required AiUsage Usage { get; init; }
}

/// <summary>
/// One proposed link, raw from the model. Names are resolved and validated against real
/// artifacts by the Application service; anything that does not resolve is dropped.
/// </summary>
public class BackfillLinkProposal
{
    public required string ArtifactAName { get; init; }
    public required string ArtifactBName { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public required string Rationale { get; init; }
    public string? Quote { get; init; }
    public decimal? Confidence { get; init; }
}
