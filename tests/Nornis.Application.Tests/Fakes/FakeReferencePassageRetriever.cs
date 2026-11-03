using Nornis.Application.Knowledge;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Returns configured passages (none by default) and records the last question.</summary>
public class FakeReferencePassageRetriever : IReferencePassageRetriever
{
    public List<KnowledgePassage> Passages { get; } = [];

    /// <summary>Answer as if the reference books couldn't be reached.</summary>
    public bool Unavailable { get; set; }

    public string? LastQuestion { get; private set; }

    public IReadOnlyList<VisibilityScope>? LastAllowedScopes { get; private set; }

    public Guid? LastAttributedUserId { get; private set; }

    public ReferenceSearchMode? LastMode { get; private set; }

    /// <summary>The books the last chosen-books call named (feature 37); null when none was made.</summary>
    public IReadOnlyList<Guid>? LastDocumentIds { get; private set; }

    public Task<ReferencePassages> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        LastQuestion = question;
        return Task.FromResult(new ReferencePassages(Passages.ToList(), Unavailable));
    }

    public Task<ReferencePassages> RetrieveForScopesAsync(
        string query, Guid worldId, IReadOnlyList<VisibilityScope> allowedScopes, Guid? attributedUserId, ReferenceSearchMode mode, CancellationToken ct)
    {
        LastQuestion = query;
        LastAllowedScopes = allowedScopes;
        LastAttributedUserId = attributedUserId;
        LastMode = mode;
        return Task.FromResult(new ReferencePassages(Passages.ToList(), Unavailable));
    }

    public Task<ReferencePassages> RetrieveFromDocumentsAsync(
        string query, Guid worldId, Guid userId, IReadOnlyList<Guid> documentIds, CancellationToken ct)
    {
        LastQuestion = query;
        LastDocumentIds = documentIds;
        return Task.FromResult(new ReferencePassages(
            Passages.Where(p => documentIds.Contains(p.DocumentId)).ToList(), Unavailable));
    }
}
