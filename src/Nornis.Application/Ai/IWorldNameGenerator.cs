namespace Nornis.Application.Ai;

/// <summary>
/// Generates an evocative fantasy world name so every demo world is named differently.
/// Implementations return null on any failure (no AI configured, timeout, bad output) —
/// the caller falls back to a static list; name generation must never fail world creation.
/// </summary>
public interface IWorldNameGenerator
{
    /// <param name="userId">Who is creating the world: there is no world yet, so their keys pay.</param>
    Task<string?> GenerateAsync(Guid userId, CancellationToken ct);
}
