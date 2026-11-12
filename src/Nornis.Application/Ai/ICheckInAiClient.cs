namespace Nornis.Application.Ai;

/// <summary>
/// The one AI call of a check-in (feature 44): given the bounded candidate list, choose the few
/// worth asking and phrase them. Mirrors <see cref="IRetrospectiveAiClient"/>: Application builds
/// the prompts and owns the interface, Infrastructure keeps transport, schema and parse, tests
/// substitute a fake.
/// </summary>
public interface ICheckInAiClient
{
    Task<CheckInAiResponse> ChooseAsync(AiPromptRequest request, CancellationToken ct);
}

public class CheckInAiResponse
{
    public required IReadOnlyList<CheckInPick> Picks { get; init; }
    public required AiUsage Usage { get; init; }
}

/// <summary>
/// One chosen candidate, as the model returned it. Raw strings throughout; the service resolves
/// the key against the candidates it sent and the record-on id against that candidate's related
/// entries, and drops what does not resolve.
/// </summary>
public class CheckInPick
{
    public required string CandidateKey { get; init; }
    public required string Question { get; init; }
    public required string Why { get; init; }
    public string? SuggestedRule { get; init; }
    public string? RecordOnArtifactId { get; init; }
}
