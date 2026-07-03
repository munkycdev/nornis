using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

public class FakeKnowledgeRetriever : IKnowledgeRetriever
{
    public KnowledgeContext? NextContext { get; set; }

    /// <summary>
    /// The number of times <see cref="RetrieveAsync"/> has been called.
    /// </summary>
    public int CallCount { get; private set; }

    public Task<KnowledgeContext> RetrieveAsync(
        string question,
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct)
    {
        CallCount++;

        return Task.FromResult(NextContext ?? new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        });
    }
}
