namespace Nornis.Application.Ai;

/// <summary>
/// Generates an evocative fantasy world name so every demo world is named differently.
/// Implementations return null on any failure (no AI configured, timeout, bad output) —
/// the caller falls back to a static list; name generation must never fail world creation.
/// </summary>
public interface IWorldNameGenerator
{
    Task<string?> GenerateAsync(CancellationToken ct);
}
