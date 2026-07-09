using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

public interface IKnowledgeRetriever
{
    Task<KnowledgeContext> RetrieveAsync(
        string question,
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct);
}
