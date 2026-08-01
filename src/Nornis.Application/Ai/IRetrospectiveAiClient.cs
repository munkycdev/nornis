namespace Nornis.Application.Ai;

/// <summary>
/// Abstraction over the AI call that assesses whether Active storylines are actually
/// finished. Mirrors <see cref="IAuditAiClient"/>: the Application layer builds the
/// prompts and owns the interface, Infrastructure provides the Azure OpenAI
/// implementation, and tests substitute a fake.
/// </summary>
public interface IRetrospectiveAiClient
{
    Task<RetrospectiveAiResponse> AssessAsync(AiPromptRequest request, CancellationToken ct);
}

public class RetrospectiveAiResponse
{
    public required IReadOnlyList<RetrospectiveVerdict> Verdicts { get; init; }
    public required AiUsage Usage { get; init; }
}

/// <summary>
/// One storyline's assessment. <see cref="StorylineId"/> is the raw string from the
/// model; the Application service resolves and validates it against real storylines.
/// </summary>
public class RetrospectiveVerdict
{
    public required string StorylineId { get; init; }
    public required string Verdict { get; init; }
    public required string Rationale { get; init; }
    public decimal? Confidence { get; init; }
}
