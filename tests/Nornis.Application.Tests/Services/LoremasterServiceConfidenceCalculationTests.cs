using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LoremasterServiceConfidenceCalculationTests
{
    [Test]
    public void DetermineConfidence_NoArtifacts_ReturnsLow()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var result = LoremasterService.DetermineConfidence(context);

        Assert.That(result, Is.EqualTo(ConfidenceLevel.Low));
    }

    [Test]
    public void DetermineConfidence_OneConfirmedFact_NoRelationships_ReturnsMedium()
    {
        var artifactId = Guid.NewGuid();

        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = artifactId,
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A sea captain in Black Harbor",
                    ReferenceId = "art-1"
                }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = artifactId,
                    Predicate = "location",
                    Value = "Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "fact-1"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var result = LoremasterService.DetermineConfidence(context);

        Assert.That(result, Is.EqualTo(ConfidenceLevel.Medium));
    }

    [Test]
    public void DetermineConfidence_TwoTotalFacts_ReturnsMedium()
    {
        var artifactId = Guid.NewGuid();

        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = artifactId,
                    Name = "Black Harbor",
                    Type = "Location",
                    Summary = "A port city",
                    ReferenceId = "art-1"
                }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = artifactId,
                    Predicate = "region",
                    Value = "Eastern Coast",
                    TruthState = TruthState.Rumor,
                    ReferenceId = "fact-1"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = artifactId,
                    Predicate = "population",
                    Value = "Several thousand",
                    TruthState = TruthState.Rumor,
                    ReferenceId = "fact-2"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var result = LoremasterService.DetermineConfidence(context);

        Assert.That(result, Is.EqualTo(ConfidenceLevel.Medium));
    }

    [Test]
    public void DetermineConfidence_ThreeConfirmedFacts_WithRelationships_WithSourceReferences_ReturnsHigh()
    {
        var artifactId = Guid.NewGuid();
        var artifactBId = Guid.NewGuid();
        var factId = Guid.NewGuid();

        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = artifactId,
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A sea captain suspected of smuggling",
                    ReferenceId = "art-1"
                },
                new()
                {
                    Id = artifactBId,
                    Name = "Black Harbor",
                    Type = "Location",
                    Summary = "A port city",
                    ReferenceId = "art-2"
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
                    ReferenceId = "fact-1"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = artifactId,
                    Predicate = "occupation",
                    Value = "Sea captain",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "fact-2"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = artifactId,
                    Predicate = "denied_knowledge",
                    Value = "Missing caravan",
                    TruthState = TruthState.Likely,
                    ReferenceId = "fact-3"
                }
            },
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactAId = artifactId,
                    ArtifactBId = artifactBId,
                    Type = "LocatedIn",
                    Description = "Captain Voss is located in Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "rel-1"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    TargetId = factId,
                    Quote = "We questioned Captain Voss in Black Harbor",
                    ReferenceId = "src-1"
                }
            }
        };

        var result = LoremasterService.DetermineConfidence(context);

        Assert.That(result, Is.EqualTo(ConfidenceLevel.High));
    }
}
