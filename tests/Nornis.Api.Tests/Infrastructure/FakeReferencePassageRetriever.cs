using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>Benign passage retriever: no library content in integration tests, and the DI
/// stub for IEmbeddingClient would otherwise throw at controller activation.</summary>
public class FakeReferencePassageRetriever : IReferencePassageRetriever
{
    public Task<ReferencePassages> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct) =>
        Task.FromResult(ReferencePassages.None);

    public Task<ReferencePassages> RetrieveForScopesAsync(
        string query, Guid worldId, IReadOnlyList<VisibilityScope> allowedScopes, Guid? attributedUserId, ReferenceSearchMode mode, CancellationToken ct) =>
        Task.FromResult(ReferencePassages.None);
}
