namespace Nornis.Application.Ai;

/// <summary>
/// Abstraction over the AI call that turns one continuity finding into concrete draft
/// proposals. Mirrors <see cref="IAuditAiClient"/>: the Application layer builds the prompts
/// and owns the interface, Infrastructure provides the Azure OpenAI implementation, and tests
/// substitute a fake.
/// </summary>
public interface IContinuityFixAiClient
{
    Task<ContinuityFixAiResponse> DraftAsync(AiPromptRequest request, CancellationToken ct);
}

public class ContinuityFixAiResponse
{
    public required IReadOnlyList<ContinuityFixProposal> Proposals { get; init; }
    public required AiUsage Usage { get; init; }
}

/// <summary>
/// One raw proposed change from the model — a flat superset of the fields the four allowed
/// change types use. Everything is loosely typed; the Application service resolves
/// <see cref="TargetRef"/> against the real record and drops anything ungrounded or empty.
/// </summary>
public class ContinuityFixProposal
{
    public required string ChangeType { get; init; }
    public required string TargetRef { get; init; }
    public required string Rationale { get; init; }

    // UpdateArtifact
    public string? Name { get; init; }
    public string? Summary { get; init; }
    public string? Status { get; init; }

    // UpdateFact / AddFact
    public string? Predicate { get; init; }
    public string? Value { get; init; }
    public string? TruthState { get; init; }

    // UpdateRelationship
    public string? RelationshipType { get; init; }
    public string? Description { get; init; }

    public decimal? Confidence { get; init; }
}
