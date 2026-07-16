using NUnit.Framework;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LoremasterServicePassageTests
{
    private static KnowledgePassage Passage(Guid? chunkId = null, Guid? documentId = null) => new()
    {
        ChunkId = chunkId ?? Guid.NewGuid(),
        DocumentId = documentId ?? Guid.NewGuid(),
        DocumentTitle = "Forbidden Depths",
        Page = 84,
        Text = "At level 8 the characters reach the sunken temple.",
        ReferenceId = $"passage:{chunkId ?? Guid.NewGuid()}",
    };

    private static KnowledgeContext ContextWith(params KnowledgePassage[] passages) => new()
    {
        Artifacts = [],
        Facts = [],
        Relationships = [],
        SourceReferences = [],
        Passages = passages,
    };

    [Test]
    public void FormatKnowledgeContext_WithPassages_EmitsPublishedReferenceSection()
    {
        var formatted = LoremasterService.FormatKnowledgeContext(ContextWith(Passage()));

        Assert.That(formatted, Does.Contain("### Published Reference"));
        Assert.That(formatted, Does.Contain("Forbidden Depths"));
        Assert.That(formatted, Does.Contain("p. 84"));
        Assert.That(formatted, Does.Contain("sunken temple"));
    }

    [Test]
    public void FormatKnowledgeContext_PassagesOnly_IsStillContent()
    {
        var formatted = LoremasterService.FormatKnowledgeContext(ContextWith(Passage()));

        Assert.That(formatted, Is.Not.Empty,
            "a context with only reference passages must still reach the prompt");
    }

    [Test]
    public void ParseCitations_PassageReference_ResolvesToPassageCitationWithDocument()
    {
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var passage = Passage(chunkId, documentId);
        var context = ContextWith(passage);

        var citations = LoremasterService.ParseCitations(
            $"The temple opens at level 8 [ref:{passage.ReferenceId}].", context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Passage));
        Assert.That(citations[0].DisplayName, Is.EqualTo("Forbidden Depths, p. 84"));
        Assert.That(citations[0].DocumentId, Is.EqualTo(documentId));
    }

    [Test]
    public void ParseCitations_UnknownPassageReference_IsDropped()
    {
        var citations = LoremasterService.ParseCitations(
            $"Something [ref:passage:{Guid.NewGuid()}].", ContextWith());

        Assert.That(citations, Is.Empty);
    }
}
