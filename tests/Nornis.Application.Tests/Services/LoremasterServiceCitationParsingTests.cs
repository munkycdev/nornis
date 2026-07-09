using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LoremasterServiceCitationParsingTests
{
    private static readonly Guid ArtifactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RelationshipId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SourceRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private KnowledgeContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = ArtifactId,
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A harbor captain with secrets",
                    ReferenceId = $"artifact:{ArtifactId}"
                }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = FactId,
                    ArtifactId = ArtifactId,
                    Predicate = "location",
                    Value = "Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = $"fact:{FactId}"
                }
            },
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = RelationshipId,
                    ArtifactAId = ArtifactId,
                    ArtifactBId = Guid.NewGuid(),
                    Type = "LocatedIn",
                    Description = "Captain Voss is located in Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = $"rel:{RelationshipId}"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>
            {
                new()
                {
                    Id = SourceRefId,
                    SourceId = Guid.NewGuid(),
                    TargetId = FactId,
                    Quote = "Voss was seen at the docks",
                    ReferenceId = $"src:{SourceRefId}"
                }
            }
        };
    }

    [Test]
    public void ParseCitations_WithValidArtifactRef_ReturnsCitationWithCorrectType()
    {
        var responseText = $"Captain Voss [ref:artifact:{ArtifactId}] is a key figure.";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Artifact));
        Assert.That(citations[0].ReferenceId, Is.EqualTo($"artifact:{ArtifactId}"));
        Assert.That(citations[0].DisplayName, Is.EqualTo("Captain Voss"));
        Assert.That(citations[0].ArtifactId, Is.EqualTo(ArtifactId));
    }

    [Test]
    public void ParseCitations_WithValidFactRef_ReturnsCitationWithCorrectType()
    {
        var responseText = $"He is based at Black Harbor [ref:fact:{FactId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Fact));
        Assert.That(citations[0].ReferenceId, Is.EqualTo($"fact:{FactId}"));
        Assert.That(citations[0].DisplayName, Is.EqualTo("location: Black Harbor"));
        Assert.That(citations[0].FactId, Is.EqualTo(FactId));
    }

    [Test]
    public void ParseCitations_WithValidRelationshipRef_ReturnsCitationWithCorrectType()
    {
        var responseText = $"They are connected [ref:rel:{RelationshipId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Relationship));
        Assert.That(citations[0].ReferenceId, Is.EqualTo($"rel:{RelationshipId}"));
        Assert.That(citations[0].DisplayName, Is.EqualTo("Captain Voss is located in Black Harbor"));
        Assert.That(citations[0].RelationshipId, Is.EqualTo(RelationshipId));
    }

    [Test]
    public void ParseCitations_WithValidSourceRef_ReturnsCitationWithCorrectType()
    {
        var responseText = $"According to records [ref:src:{SourceRefId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Source));
        Assert.That(citations[0].ReferenceId, Is.EqualTo($"src:{SourceRefId}"));
        Assert.That(citations[0].DisplayName, Is.EqualTo("Voss was seen at the docks"));
        Assert.That(citations[0].SourceId, Is.EqualTo(SourceRefId));
    }

    [Test]
    public void ParseCitations_WithUnknownRef_SilentlyDropsIt()
    {
        var unknownId = Guid.NewGuid();
        var responseText = $"Something [ref:artifact:{unknownId}] unknown.";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Is.Empty);
    }

    [Test]
    public void ParseCitations_WithMixedValidAndInvalidRefs_ReturnsOnlyValidCitations()
    {
        var unknownId = Guid.NewGuid();
        var responseText = $"Captain Voss [ref:artifact:{ArtifactId}] is linked [ref:artifact:{unknownId}] to the harbor [ref:fact:{FactId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(2));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Artifact));
        Assert.That(citations[1].Type, Is.EqualTo(CitationType.Fact));
    }

    [Test]
    public void ParseCitations_WithDuplicateRefs_DeduplicatesByReferenceId()
    {
        var responseText = $"Captain Voss [ref:artifact:{ArtifactId}] is mentioned again [ref:artifact:{ArtifactId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].ReferenceId, Is.EqualTo($"artifact:{ArtifactId}"));
    }

    [Test]
    public void ParseCitations_WithEmptyResponseText_ReturnsEmptyList()
    {
        var citations = LoremasterService.ParseCitations("", _context);

        Assert.That(citations, Is.Empty);
    }

    [Test]
    public void ParseCitations_WithNullResponseText_ReturnsEmptyList()
    {
        var citations = LoremasterService.ParseCitations(null!, _context);

        Assert.That(citations, Is.Empty);
    }

    [Test]
    public void ParseCitations_WithNoCitationMarkers_ReturnsEmptyList()
    {
        var responseText = "Just a plain answer with no citations at all.";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Is.Empty);
    }

    [Test]
    public void ParseCitations_WithAllFourTypes_ReturnsAllValidCitations()
    {
        var responseText = $"Voss [ref:artifact:{ArtifactId}] at harbor [ref:fact:{FactId}] connected [ref:rel:{RelationshipId}] per source [ref:src:{SourceRefId}].";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(4));
        Assert.That(citations.Select(c => c.Type), Is.EquivalentTo(new[]
        {
            CitationType.Artifact,
            CitationType.Fact,
            CitationType.Relationship,
            CitationType.Source
        }));
    }

    [Test]
    public void ParseCitations_WithMalformedMarkers_IgnoresThem()
    {
        var responseText = "Broken [ref: and [ref] and [ref:] markers.";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        // [ref:] matches the regex with empty content, but won't match any known ID
        // [ref: and [ref] won't match the regex at all
        Assert.That(citations, Is.Empty);
    }

    [Test]
    public void ParseCitations_RelationshipWithoutDescription_UsesTypeAsDisplayName()
    {
        var relId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = relId,
                    ArtifactAId = Guid.NewGuid(),
                    ArtifactBId = Guid.NewGuid(),
                    Type = "AlliedWith",
                    Description = null,
                    TruthState = TruthState.Confirmed,
                    ReferenceId = $"rel:{relId}"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var responseText = $"They are allies [ref:rel:{relId}].";

        var citations = LoremasterService.ParseCitations(responseText, context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].DisplayName, Is.EqualTo("AlliedWith"));
    }

    [Test]
    public void ParseCitations_SourceReferenceWithoutQuote_UsesFallbackDisplayName()
    {
        var srcId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>
            {
                new()
                {
                    Id = srcId,
                    SourceId = Guid.NewGuid(),
                    TargetId = Guid.NewGuid(),
                    Quote = null,
                    ReferenceId = $"src:{srcId}"
                }
            }
        };

        var responseText = $"According to records [ref:src:{srcId}].";

        var citations = LoremasterService.ParseCitations(responseText, context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].DisplayName, Is.EqualTo($"Source reference {srcId}"));
    }

    [Test]
    public void ParseCitations_PreservesMarkerOrderInOutput()
    {
        var responseText = $"Start [ref:fact:{FactId}] then [ref:artifact:{ArtifactId}] end.";

        var citations = LoremasterService.ParseCitations(responseText, _context);

        Assert.That(citations, Has.Count.EqualTo(2));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Fact));
        Assert.That(citations[1].Type, Is.EqualTo(CitationType.Artifact));
    }

    [Test]
    public async Task AskAsync_AnswerTextPreservesCitationMarkersForUiRendering()
    {
        // Arrange: set up the full service with fakes
        var knowledgeRetriever = new FakeKnowledgeRetriever();
        var aiClient = new FakeLoremasterAiClient();
        var usageRepository = new InMemoryAiUsageRecordRepository();
        var options = Options.Create(new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000
        });

        var service = new LoremasterService(knowledgeRetriever, aiClient, usageRepository, new FakeAiBudgetGuard(), options);

        var artifactId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var artRef = $"artifact:{artifactId}";
        var factRef = $"fact:{factId}";

        knowledgeRetriever.NextContext = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = artifactId,
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A harbor captain",
                    ReferenceId = artRef
                }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = factId,
                    ArtifactId = artifactId,
                    Predicate = "location",
                    Value = "Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = factRef
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        // AI returns answer with citation markers inline
        var answerWithMarkers = $"Captain Voss [ref:{artRef}] is located in Black Harbor [ref:{factRef}].";
        aiClient.SetupSuccess(answerWithMarkers);

        var command = new AskLoremasterCommand(
            CampaignId: Guid.NewGuid(),
            Question: "Where is Captain Voss?",
            UserId: Guid.NewGuid(),
            UserRole: CampaignRole.GM,
            ConversationContext: null);

        // Act
        var result = await service.AskAsync(command, CancellationToken.None);

        // Assert: answer text preserves citation markers for UI rendering
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AnswerText, Does.Contain($"[ref:{artRef}]"));
        Assert.That(result.Value!.AnswerText, Does.Contain($"[ref:{factRef}]"));
        Assert.That(result.Value!.AnswerText, Is.EqualTo(answerWithMarkers));
    }
}
