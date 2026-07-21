namespace Nornis.Application.Ai;

/// <summary>
/// Abstraction over the AI call that turns one continuity finding into concrete draft
/// proposals. Mirrors <see cref="IAuditAiClient"/>: the Application layer builds the prompts
/// and owns the interface, Infrastructure provides the Azure OpenAI implementation, and tests
/// substitute a fake.
/// </summary>
public interface IContinuityFixAiClient
{
    Task<ContinuityFixAiResponse> DraftAsync(ContinuityFixAiRequest request, CancellationToken ct);
}

public class ContinuityFixAiRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public required string Model { get; init; }
    public required int TimeoutSeconds { get; init; }
}

public class ContinuityFixAiResponse
{
    public required IReadOnlyList<ContinuityFixProposal> Proposals { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
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
