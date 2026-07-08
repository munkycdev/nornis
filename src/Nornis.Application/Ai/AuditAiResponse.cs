namespace Nornis.Application.Ai;

/// <summary>
/// The raw result of a continuity-audit AI call. Findings arrive as loosely-typed
/// <see cref="AuditFinding"/> records; the Application service validates them (resolving
/// evidence ids, dropping the ungrounded, capping the count) before persisting.
/// </summary>
public class AuditAiResponse
{
    public required IReadOnlyList<AuditFinding> Findings { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
}
