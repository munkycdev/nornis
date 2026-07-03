using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

public interface IKnowledgeRetriever
{
    Task<KnowledgeContext> RetrieveAsync(
        string question,
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct);
}
