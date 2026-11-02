using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>A real retriever with a record of what it was asked, and at which role.</summary>
public sealed class SpyKnowledgeRetriever(IKnowledgeRetriever inner) : IKnowledgeRetriever
{
    public IKnowledgeRetriever Inner { get; } = inner;

    public List<(string Text, WorldRole Role)> Calls { get; } = [];

    public Task<KnowledgeContext> RetrieveAsync(string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        Calls.Add((question, role));
        return Inner.RetrieveAsync(question, worldId, userId, role, ct);
    }
}
