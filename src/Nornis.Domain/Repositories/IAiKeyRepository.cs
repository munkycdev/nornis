using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

/// <summary>A user's AI keys, their OpenAI-compatible model lines, and their model choices (feature 39).</summary>
public interface IAiKeyRepository
{
    /// <summary>The user's keys with their compatible model lines, oldest first.</summary>
    Task<IReadOnlyList<UserAiKey>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>One key with its compatible model lines, or null when it is gone.</summary>
    Task<UserAiKey?> GetAsync(Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>The user's model for a task, or null for the task's default.</summary>
    Task<UserAiModelChoice?> GetChoiceAsync(Guid userId, AiTask task, CancellationToken cancellationToken = default);

    /// <summary>The user's model choices, one per task they picked.</summary>
    Task<IReadOnlyList<UserAiModelChoice>> ListChoicesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the key refused by its provider, unless it was saved again after
    /// <paramref name="keyVersion"/>: a call made with the old key must not fail the new one.
    /// </summary>
    Task MarkCheckFailedAsync(Guid keyId, DateTimeOffset keyVersion, DateTimeOffset at, CancellationToken cancellationToken = default);
}
