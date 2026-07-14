namespace Nornis.Application.Ai;

/// <summary>
/// Abstraction over the AI call that assesses whether Active storylines are actually
/// finished. Mirrors <see cref="IAuditAiClient"/>: the Application layer builds the
/// prompts and owns the interface, Infrastructure provides the Azure OpenAI
/// implementation, and tests substitute a fake.
/// </summary>
public interface IRetrospectiveAiClient
{
    Task<RetrospectiveAiResponse> AssessAsync(RetrospectiveAiRequest request, CancellationToken ct);
}

public class RetrospectiveAiRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public required string Model { get; init; }
    public required int TimeoutSeconds { get; init; }
}

public class RetrospectiveAiResponse
{
    public required IReadOnlyList<RetrospectiveVerdict> Verdicts { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
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
