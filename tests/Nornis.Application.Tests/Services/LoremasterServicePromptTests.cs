using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LoremasterServicePromptTests
{
    private LoremasterService _service = null!;
    private FakeKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private LoremasterOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _knowledgeRetriever = new FakeKnowledgeRetriever();
        _aiClient = new FakeLoremasterAiClient();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();
        _options = new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000
        };

        _service = new LoremasterService(_knowledgeRetriever, _aiClient, _aiUsageRecordRepository, Options.Create(_options));
    }

    [Test]
    public void SystemPromptTemplate_ContainsLoremasterPersona()
    {
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("Loremaster"));
    }

    [Test]
    public void SystemPromptTemplate_ContainsGroundingRules()
    {
        Assert.That(LoremasterService.SystemPromptTemplate,
            Does.Contain("Ground every answer exclusively in the provided campaign knowledge context"));
    }

    [Test]
    public void SystemPromptTemplate_ContainsCitationFormat()
    {
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("[ref:ID]"));
    }

    [Test]
    public void SystemPromptTemplate_ContainsTruthStateHandling()
    {
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("Rumor"));
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("Disputed"));
    }

    [Test]
    public void SystemPromptTemplate_ContainsAntiHallucinationInstructions()
    {
        Assert.That(LoremasterService.SystemPromptTemplate,
            Does.Contain("Do not invent campaign facts"));
    }

    [Test]
    public void SystemPromptTemplate_InstructsToAcknowledgeMissingInfo()
    {
        Assert.That(LoremasterService.SystemPromptTemplate,
            Does.Contain("say so plainly"));
    }

    [Test]
    public void BuildPrompt_IncludesQuestionInUserMessage()
    {
        var context = CreateEmptyContext();
        var question = "Who is Captain Voss?";

        var request = _service.BuildPrompt(question, context);

        Assert.That(request.UserMessage, Does.Contain(question));
    }

    [Test]
    public void BuildPrompt_IncludesArtifactNamesInUserMessage()
    {
        var context = CreateContextWithArtifact("Captain Voss", "Character", "A sea captain in Black Harbor");

        var request = _service.BuildPrompt("Tell me about Voss", context);

        Assert.That(request.UserMessage, Does.Contain("Captain Voss"));
    }

    [Test]
    public void BuildPrompt_IncludesArtifactTypeAndSummary()
    {
        var context = CreateContextWithArtifact("Black Harbor", "Location", "A port city on the coast");

        var request = _service.BuildPrompt("Where is Black Harbor?", context);

        Assert.That(request.UserMessage, Does.Contain("Location"));
        Assert.That(request.UserMessage, Does.Contain("A port city on the coast"));
    }

    [Test]
    public void BuildPrompt_IncludesArtifactReferenceId()
    {
        var context = CreateContextWithArtifact("Silver Key", "Item", "A mysterious key");

        var request = _service.BuildPrompt("What is the Silver Key?", context);

        Assert.That(request.UserMessage, Does.Contain("[ref:art-1]"));
    }

    [Test]
    public void BuildPrompt_IncludesFactsWithReferenceIds()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "location",
                    Value = "Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "fact-1"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Where is Voss?", context);

        Assert.That(request.UserMessage, Does.Contain("location: Black Harbor"));
        Assert.That(request.UserMessage, Does.Contain("[ref:fact-1]"));
    }

    [Test]
    public void BuildPrompt_LabelsFacts_WithRumorTruthState()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "allegiance",
                    Value = "The Shadow Guild",
                    TruthState = TruthState.Rumor,
                    ReferenceId = "fact-2"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Who does Voss work for?", context);

        Assert.That(request.UserMessage, Does.Contain("[Rumor]"));
    }

    [Test]
    public void BuildPrompt_LabelsFacts_WithDisputedTruthState()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "involvement",
                    Value = "Voss stole the caravan",
                    TruthState = TruthState.Disputed,
                    ReferenceId = "fact-3"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Did Voss steal the caravan?", context);

        Assert.That(request.UserMessage, Does.Contain("[Disputed]"));
    }

    [Test]
    public void BuildPrompt_ConfirmedFacts_DoNotHaveTruthStateLabel()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "name",
                    Value = "Captain Voss",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "fact-4"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Who is Voss?", context);

        Assert.That(request.UserMessage, Does.Not.Contain("[Confirmed]"));
        Assert.That(request.UserMessage, Does.Not.Contain("[Rumor]"));
        Assert.That(request.UserMessage, Does.Not.Contain("[Disputed]"));
    }

    [Test]
    public void BuildPrompt_IncludesRelationshipsWithReferenceIds()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactAId = Guid.NewGuid(),
                    ArtifactBId = Guid.NewGuid(),
                    Type = "LocatedIn",
                    Description = "Captain Voss is located in Black Harbor",
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "rel-1"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Where is Voss?", context);

        Assert.That(request.UserMessage, Does.Contain("LocatedIn"));
        Assert.That(request.UserMessage, Does.Contain("Captain Voss is located in Black Harbor"));
        Assert.That(request.UserMessage, Does.Contain("[ref:rel-1]"));
    }

    [Test]
    public void BuildPrompt_LabelsRelationships_WithRumorTruthState()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactAId = Guid.NewGuid(),
                    ArtifactBId = Guid.NewGuid(),
                    Type = "AlliedWith",
                    Description = "Voss may be allied with the smugglers",
                    TruthState = TruthState.Rumor,
                    ReferenceId = "rel-2"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var request = _service.BuildPrompt("Who are Voss's allies?", context);

        Assert.That(request.UserMessage, Does.Contain("[Rumor]"));
    }

    [Test]
    public void BuildPrompt_IncludesSourceReferencesWithQuotes()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = sourceId,
                    TargetId = targetId,
                    Quote = "Tavrin found the Silver Key in his quarters",
                    ReferenceId = "src-1"
                }
            }
        };

        var request = _service.BuildPrompt("What about the Silver Key?", context);

        Assert.That(request.UserMessage, Does.Contain("Tavrin found the Silver Key in his quarters"));
        Assert.That(request.UserMessage, Does.Contain("[ref:src-1]"));
    }

    [Test]
    public void BuildPrompt_SetsModelFromOptions()
    {
        var context = CreateEmptyContext();

        var request = _service.BuildPrompt("Any question", context);

        Assert.That(request.Model, Is.EqualTo("gpt-4o"));
    }

    [Test]
    public void BuildPrompt_SetsTimeoutFromOptions()
    {
        var context = CreateEmptyContext();

        var request = _service.BuildPrompt("Any question", context);

        Assert.That(request.TimeoutSeconds, Is.EqualTo(30));
    }

    [Test]
    public void BuildPrompt_UsesSystemPromptTemplate()
    {
        var context = CreateEmptyContext();

        var request = _service.BuildPrompt("Any question", context);

        Assert.That(request.SystemPrompt, Is.EqualTo(LoremasterService.SystemPromptTemplate));
    }

    [Test]
    public void BuildPrompt_EmptyContext_StillIncludesQuestion()
    {
        var context = CreateEmptyContext();
        var question = "What do we know about the missing caravan?";

        var request = _service.BuildPrompt(question, context);

        Assert.That(request.UserMessage, Does.Contain(question));
    }

    [Test]
    public void BuildPrompt_EmptyContext_DoesNotIncludeKnowledgeHeader()
    {
        var context = CreateEmptyContext();

        var request = _service.BuildPrompt("Any question", context);

        Assert.That(request.UserMessage, Does.Not.Contain("Campaign Knowledge Context"));
    }

    [Test]
    public void BuildPrompt_NonEmptyContext_IncludesKnowledgeHeader()
    {
        var context = CreateContextWithArtifact("Captain Voss", "Character", "A captain");

        var request = _service.BuildPrompt("Who is Voss?", context);

        Assert.That(request.UserMessage, Does.Contain("Campaign Knowledge Context"));
    }

    [Test]
    public void FormatKnowledgeContext_SourceReference_WithNoQuote_ShowsNoQuoteLabel()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    TargetId = Guid.NewGuid(),
                    Quote = null,
                    ReferenceId = "src-2"
                }
            }
        };

        var result = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(result, Does.Contain("(no quote)"));
        Assert.That(result, Does.Contain("[ref:src-2]"));
    }

    [Test]
    public void FormatKnowledgeContext_RelationshipWithoutDescription_OmitsDescription()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactAId = Guid.NewGuid(),
                    ArtifactBId = Guid.NewGuid(),
                    Type = "LocatedIn",
                    Description = null,
                    TruthState = TruthState.Confirmed,
                    ReferenceId = "rel-3"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var result = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(result, Does.Contain("LocatedIn [ref:rel-3]"));
        Assert.That(result, Does.Not.Contain("—"));
    }

    #region Conversation context and structured formatting

    [Test]
    public void BuildPrompt_WithConversationContext_IncludesConversationSection()
    {
        var context = CreateEmptyContext();
        var conversation = "Q: Who is Captain Voss?\nA: A harbor captain in Black Harbor.";

        var request = _service.BuildPrompt("What about his brother?", context, conversation);

        Assert.That(request.UserMessage, Does.Contain("## Conversation So Far"));
        Assert.That(request.UserMessage, Does.Contain("Who is Captain Voss?"));
    }

    [Test]
    public void BuildPrompt_WithoutConversationContext_OmitsConversationSection()
    {
        var request = _service.BuildPrompt("Who is Voss?", CreateEmptyContext());

        Assert.That(request.UserMessage, Does.Not.Contain("## Conversation So Far"));
    }

    [Test]
    public async Task AskAsync_ConversationContext_ReachesAiPromptAndRetrieval()
    {
        var command = new Nornis.Application.Models.AskLoremasterCommand(
            Guid.NewGuid(),
            "What about his brother?",
            Guid.NewGuid(),
            CampaignRole.Player,
            "Q: Who is Captain Voss? A: A harbor captain.");

        var result = await _service.AskAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_aiClient.LastRequest!.UserMessage, Does.Contain("## Conversation So Far"));
        Assert.That(_knowledgeRetriever.LastQuestion, Does.Contain("Captain Voss"),
            "conversation context should participate in retrieval name matching");
    }

    [Test]
    public void FormatKnowledgeContext_GroupsFactsUnderTheirArtifact()
    {
        var artifactId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = artifactId, Name = "Captain Voss", Type = "Character", Summary = "A captain", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>
            {
                new() { Id = Guid.NewGuid(), ArtifactId = artifactId, Predicate = "location", Value = "Black Harbor", TruthState = TruthState.Confirmed, ReferenceId = "fact-1" }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        var artifactLine = formatted.IndexOf("Captain Voss (Character", StringComparison.Ordinal);
        var factLine = formatted.IndexOf("location: Black Harbor", StringComparison.Ordinal);
        Assert.That(artifactLine, Is.GreaterThanOrEqualTo(0));
        Assert.That(factLine, Is.GreaterThan(artifactLine), "fact should be nested under its artifact");
        Assert.That(formatted, Does.Not.Contain("### Additional Facts"));
    }

    [Test]
    public void FormatKnowledgeContext_NamesRelationshipEndpoints()
    {
        var vossId = Guid.NewGuid();
        var harborId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = vossId, Name = "Captain Voss", Type = "Character", ReferenceId = "art-1" },
                new() { Id = harborId, Name = "Black Harbor", Type = "Location", ReferenceId = "art-2" }
            },
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new() { Id = Guid.NewGuid(), ArtifactAId = vossId, ArtifactBId = harborId, Type = "LocatedIn", TruthState = TruthState.Confirmed, ReferenceId = "rel-1" }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("Captain Voss <-> Black Harbor: LocatedIn"));
    }

    [Test]
    public void FormatKnowledgeContext_LabelsFalseAndHiddenTruthStates()
    {
        var artifactId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = artifactId, Name = "Captain Voss", Type = "Character", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>
            {
                new() { Id = Guid.NewGuid(), ArtifactId = artifactId, Predicate = "allegiance", Value = "The Crown", TruthState = TruthState.False, ReferenceId = "fact-1" },
                new() { Id = Guid.NewGuid(), ArtifactId = artifactId, Predicate = "true allegiance", Value = "The Shadow Guild", TruthState = TruthState.Hidden, ReferenceId = "fact-2" }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("[False \u2014 recorded misinformation]"));
        Assert.That(formatted, Does.Contain("[Hidden \u2014 GM-only truth]"));
    }

    [Test]
    public void FormatKnowledgeContext_OrphanFacts_ListedUnderAdditionalFacts()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>
            {
                new() { Id = Guid.NewGuid(), ArtifactId = Guid.NewGuid(), Predicate = "location", Value = "Black Harbor", TruthState = TruthState.Confirmed, ReferenceId = "fact-1" }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("### Additional Facts"));
        Assert.That(formatted, Does.Contain("location: Black Harbor"));
    }

    [Test]
    public void FormatKnowledgeContext_IncludesArtifactStatus_WhenPresent()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "The Missing Caravan", Type = "Storyline", Status = "Dormant", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("The Missing Caravan (Storyline, Dormant)"));
    }

    [Test]
    public void AssembleCaveats_HiddenFacts_AddGmOnlyCaveat()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Voss", Type = "Character", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>
            {
                new() { Id = Guid.NewGuid(), ArtifactId = Guid.NewGuid(), Predicate = "secret", Value = "traitor", TruthState = TruthState.Hidden, ReferenceId = "fact-1" }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };

        var caveats = LoremasterService.AssembleCaveats(context);

        Assert.That(caveats, Does.Contain("Includes GM-only knowledge not visible to players"));
    }

    #endregion

    private static KnowledgeContext CreateEmptyContext() => new()
    {
        Artifacts = new List<KnowledgeArtifact>(),
        Facts = new List<KnowledgeFact>(),
        Relationships = new List<KnowledgeRelationship>(),
        SourceReferences = new List<KnowledgeSourceReference>()
    };

    private static KnowledgeContext CreateContextWithArtifact(string name, string type, string summary) => new()
    {
        Artifacts = new List<KnowledgeArtifact>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = type,
                Summary = summary,
                ReferenceId = "art-1"
            }
        },
        Facts = new List<KnowledgeFact>(),
        Relationships = new List<KnowledgeRelationship>(),
        SourceReferences = new List<KnowledgeSourceReference>()
    };
}
