using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for LoremasterService — knowledge retrieval and prompt construction.
/// Requirements: 4.1, 5.1, 5.3, 5.4, 6.1, 6.2, 6.3, 8.3
/// </summary>
[TestFixture]
public class LoremasterServiceKnowledgeRetrievalAndPromptTests
{
    private IKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private LoremasterService _service = null!;

    // Realistic test data
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid KeldaUserId = Guid.NewGuid();
    private static readonly Guid TavrinUserId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _knowledgeRetriever = Substitute.For<IKnowledgeRetriever>();
        _aiClient = new FakeLoremasterAiClient();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();

        var options = new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 10.00m }
            }
        };

        _service = new LoremasterService(
            _knowledgeRetriever, new FakeReferencePassageRetriever(),
            _aiClient,
            _aiUsageRecordRepository,
            new FakeAiBudgetGuard(), Options.Create(options));
    }

    #region Knowledge Retrieval Tests

    [Test]
    public async Task AskAsync_ValidQuestion_TriggersRetrievalWithCorrectWorldId()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("Who is Captain Voss?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            WorldId,
            Arg.Any<Guid>(),
            Arg.Any<WorldRole>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_ValidQuestion_TriggersRetrievalWithCorrectUserId()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("Where is Black Harbor?", WorldId, TavrinUserId, WorldRole.Player);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            TavrinUserId,
            Arg.Any<WorldRole>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_ValidQuestion_TriggersRetrievalWithCorrectRole()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("What is the Silver Key?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            WorldRole.GM,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_PlayerRole_PassesPlayerRoleToRetriever()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("What do we know about the caravan?", WorldId, TavrinUserId, WorldRole.Player);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            WorldRole.Player,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_ValidQuestion_PassesQuestionTextToRetriever()
    {
        // Arrange
        var question = "Who is Captain Voss and where can I find him?";
        SetupEmptyKnowledgeContext();
        var command = CreateCommand(question, WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            question,
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<WorldRole>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Empty Knowledge Context Tests

    [Test]
    public async Task AskAsync_EmptyKnowledgeContext_ReturnsLowConfidence()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("Tell me about the lost temple", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        var result = await _service.AskAsync(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Confidence, Is.EqualTo(ConfidenceLevel.Low));
    }

    [Test]
    public async Task AskAsync_EmptyKnowledgeContext_IncludesLimitedInfoCaveat()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("What is the Shadow Guild?", WorldId, TavrinUserId, WorldRole.Player);

        // Act
        var result = await _service.AskAsync(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Caveats, Has.Some.Contain("Limited information"));
    }

    [Test]
    public async Task AskAsync_EmptyKnowledgeContext_StillCallsAiClient()
    {
        // Arrange
        SetupEmptyKnowledgeContext();
        var command = CreateCommand("Anything about the old ruins?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        Assert.That(_aiClient.CallCount, Is.EqualTo(1));
    }

    #endregion

    #region System Prompt Tests

    [Test]
    public async Task AskAsync_SystemPrompt_ContainsGroundingInstructions()
    {
        // Arrange
        SetupContextWithArtifacts();
        var command = CreateCommand("Who is Captain Voss?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.SystemPrompt, Does.Contain("Ground every answer exclusively in the provided world knowledge context"));
    }

    [Test]
    public async Task AskAsync_SystemPrompt_ContainsCitationFormat()
    {
        // Arrange
        SetupContextWithArtifacts();
        var command = CreateCommand("Where is Black Harbor?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.SystemPrompt, Does.Contain("[ref:ID]"));
    }

    [Test]
    public async Task AskAsync_SystemPrompt_ContainsAntiHallucinationRules()
    {
        // Arrange
        SetupContextWithArtifacts();
        var command = CreateCommand("Tell me about the missing caravan", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.SystemPrompt, Does.Contain("Do not invent world facts"));
        Assert.That(sentRequest.SystemPrompt, Does.Contain("Do not invent world facts, events, names, or relationships not present in the context"));
    }

    #endregion

    #region Prompt Content Tests

    [Test]
    public async Task AskAsync_Prompt_IncludesOriginalQuestionText()
    {
        // Arrange
        var question = "Who is Captain Voss and what is his role in Black Harbor?";
        SetupContextWithArtifacts();
        var command = CreateCommand(question, WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain(question));
    }

    [Test]
    public async Task AskAsync_Prompt_IncludesArtifactNamesFromKnowledgeContext()
    {
        // Arrange
        var artifactName = "Captain Voss";
        SetupContextWithNamedArtifact(artifactName, "Character", "A sea captain in Black Harbor");
        var command = CreateCommand("Who is Captain Voss?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain(artifactName));
    }

    [Test]
    public async Task AskAsync_Prompt_IncludesMultipleArtifactNames()
    {
        // Arrange
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Captain Voss", Type = "Character", Summary = "A sea captain", ReferenceId = "art-1" },
                new() { Id = Guid.NewGuid(), Name = "Black Harbor", Type = "Location", Summary = "A port city", ReferenceId = "art-2" },
                new() { Id = Guid.NewGuid(), Name = "Silver Key", Type = "Item", Summary = "A mysterious key", ReferenceId = "art-3" }
            },
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
        var command = CreateCommand("What connections exist between Voss and the key?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain("Captain Voss"));
        Assert.That(sentRequest.UserMessage, Does.Contain("Black Harbor"));
        Assert.That(sentRequest.UserMessage, Does.Contain("Silver Key"));
    }

    #endregion

    #region Truth State Label Tests

    [Test]
    public async Task AskAsync_RumorFacts_HaveTruthStateLabelInPrompt()
    {
        // Arrange
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Captain Voss", Type = "Character", Summary = "A sea captain", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "allegiance",
                    Value = "The Shadow Guild",
                    TruthState = TruthState.Rumor,
                    ReferenceId = "fact-1"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
        var command = CreateCommand("Who does Voss work for?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain("[Rumor]"));
    }

    [Test]
    public async Task AskAsync_DisputedFacts_HaveTruthStateLabelInPrompt()
    {
        // Arrange
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Captain Voss", Type = "Character", Summary = "A sea captain", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactId = Guid.NewGuid(),
                    Predicate = "involvement",
                    Value = "Planned the caravan heist",
                    TruthState = TruthState.Disputed,
                    ReferenceId = "fact-2"
                }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
        var command = CreateCommand("Was Voss involved in the heist?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain("[Disputed]"));
    }

    [Test]
    public async Task AskAsync_RumorRelationships_HaveTruthStateLabelInPrompt()
    {
        // Arrange
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Captain Voss", Type = "Character", Summary = "A sea captain", ReferenceId = "art-1" }
            },
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
                    ReferenceId = "rel-1"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
        var command = CreateCommand("Who are Voss's allies?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain("[Rumor]"));
    }

    [Test]
    public async Task AskAsync_DisputedRelationships_HaveTruthStateLabelInPrompt()
    {
        // Arrange
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Black Harbor", Type = "Location", Summary = "A port city", ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArtifactAId = Guid.NewGuid(),
                    ArtifactBId = Guid.NewGuid(),
                    Type = "Controls",
                    Description = "The faction controls this port",
                    TruthState = TruthState.Disputed,
                    ReferenceId = "rel-2"
                }
            },
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
        var command = CreateCommand("Who controls Black Harbor?", WorldId, KeldaUserId, WorldRole.GM);

        // Act
        await _service.AskAsync(command, CancellationToken.None);

        // Assert
        var sentRequest = _aiClient.LastRequest!;
        Assert.That(sentRequest.UserMessage, Does.Contain("[Disputed]"));
    }

    #endregion

    #region Helper Methods

    private static AskLoremasterCommand CreateCommand(
        string question,
        Guid worldId,
        Guid userId,
        WorldRole role) =>
        new(worldId, question, userId, role, ConversationContext: null);

    private void SetupEmptyKnowledgeContext()
    {
        var emptyContext = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>(),
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        _knowledgeRetriever.RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<WorldRole>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(emptyContext));
    }

    private void SetupContextWithArtifacts()
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = "Captain Voss", Type = "Character", Summary = "A sea captain in Black Harbor", ReferenceId = "art-1" },
                new() { Id = Guid.NewGuid(), Name = "Black Harbor", Type = "Location", Summary = "A port city on the coast", ReferenceId = "art-2" }
            },
            Facts = new List<KnowledgeFact>
            {
                new() { Id = Guid.NewGuid(), ArtifactId = Guid.NewGuid(), Predicate = "location", Value = "Black Harbor", TruthState = TruthState.Confirmed, ReferenceId = "fact-1" }
            },
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
    }

    private void SetupContextWithNamedArtifact(string name, string type, string summary)
    {
        var context = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new() { Id = Guid.NewGuid(), Name = name, Type = type, Summary = summary, ReferenceId = "art-1" }
            },
            Facts = new List<KnowledgeFact>(),
            Relationships = new List<KnowledgeRelationship>(),
            SourceReferences = new List<KnowledgeSourceReference>()
        };
        SetupKnowledgeContext(context);
    }

    private void SetupKnowledgeContext(KnowledgeContext context)
    {
        _knowledgeRetriever.RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<WorldRole>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(context));
    }

    #endregion
}
