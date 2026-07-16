using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>Benign passage retriever: no library content in integration tests, and the DI
/// stub for IEmbeddingClient would otherwise throw at controller activation.</summary>
public class FakeReferencePassageRetriever : IReferencePassageRetriever
{
    public Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KnowledgePassage>>([]);
}
