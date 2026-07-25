namespace Nornis.Application.Ai;

/// <summary>
/// Registered when no Azure OpenAI is configured: always returns null, so demo world
/// naming falls back to the static list instead of failing like the throwing AI stubs —
/// a missing AI account must not break demo world creation.
/// </summary>
public class NoOpWorldNameGenerator : IWorldNameGenerator
{
    public Task<string?> GenerateAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}
