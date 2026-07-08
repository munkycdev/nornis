namespace Nornis.Application.Ai;

/// <summary>
/// Abstraction over the AI call that assesses a campaign's semantic continuity. Mirrors
/// <see cref="ILoremasterAiClient"/>: the Application layer owns the interface, Infrastructure
/// provides the Azure OpenAI implementation, and tests substitute a fake.
/// </summary>
public interface IAuditAiClient
{
    Task<AuditAiResponse> AssessAsync(AuditAiRequest request, CancellationToken ct);
}
