namespace Nornis.Application.Ai;

/// <summary>
/// Writes the why-now beside candidates the gauge has already ranked. The prompt text belongs
/// to the Application layer; this seam owns transport, timeout, and parse.
/// </summary>
public interface IConvergenceNarrationClient
{
    Task<ConvergenceNarrationAiResponse> NarrateAsync(AiPromptRequest request, CancellationToken ct);
}

/// <summary>One candidate's sentence, keyed by the id the prompt gave it.</summary>
public record ConvergenceNarration(Guid CandidateId, string Rationale);

public class ConvergenceNarrationAiResponse
{
    public required IReadOnlyList<ConvergenceNarration> Narrations { get; init; }

    /// <summary>
    /// Null when no call was made. Distinct from a zero-token usage, which would put a phantom
    /// row in the spend ledger for work that never happened.
    /// </summary>
    public required AiUsage? Usage { get; init; }
}

/// <summary>
/// For a host with no Azure OpenAI configured. Narration degrades rather than throws, unlike
/// the other AI clients' stubs: the gauge's whole value is the mechanical ranking, and a
/// throwing stub would take the ranking down with the annotation that decorates it. Same
/// choice, and the same reason, as <see cref="NoOpWorldNameGenerator"/>.
/// </summary>
public sealed class NoOpConvergenceNarrationClient : IConvergenceNarrationClient
{
    public Task<ConvergenceNarrationAiResponse> NarrateAsync(AiPromptRequest request, CancellationToken ct) =>
        Task.FromResult(new ConvergenceNarrationAiResponse
        {
            Narrations = [],
            Usage = null
        });
}
