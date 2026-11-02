using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

/// <summary>
/// Retrieves nothing, for callers that genuinely have no library to ground against.
/// Follows <see cref="Ai.NoOpWorldNameGenerator"/>: a caller without the feature says so by
/// passing this, rather than by passing null and leaving every reader of the field to work out
/// whether null meant "off" or "nobody wired it up".
/// </summary>
public sealed class NoOpReferencePassageRetriever : IReferencePassageRetriever
{
    public static readonly NoOpReferencePassageRetriever Instance = new();

    public Task<ReferencePassages> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct) =>
        Task.FromResult(ReferencePassages.None);

    public Task<ReferencePassages> RetrieveForScopesAsync(
        string query, Guid worldId, IReadOnlyList<VisibilityScope> allowedScopes,
        Guid? attributedUserId, ReferenceSearchMode mode, CancellationToken ct) =>
        Task.FromResult(ReferencePassages.None);
}
