using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Returns configured passages (none by default) and records the last question.</summary>
public class FakeReferencePassageRetriever : IReferencePassageRetriever
{
    public List<KnowledgePassage> Passages { get; } = [];

    public string? LastQuestion { get; private set; }

    public IReadOnlyList<VisibilityScope>? LastAllowedScopes { get; private set; }

    public Guid? LastAttributedUserId { get; private set; }

    public Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        LastQuestion = question;
        return Task.FromResult<IReadOnlyList<KnowledgePassage>>(Passages.ToList());
    }

    public Task<IReadOnlyList<KnowledgePassage>> RetrieveForScopesAsync(
        string query, Guid worldId, IReadOnlyList<VisibilityScope> allowedScopes, Guid? attributedUserId, CancellationToken ct)
    {
        LastQuestion = query;
        LastAllowedScopes = allowedScopes;
        LastAttributedUserId = attributedUserId;
        return Task.FromResult<IReadOnlyList<KnowledgePassage>>(Passages.ToList());
    }
}
