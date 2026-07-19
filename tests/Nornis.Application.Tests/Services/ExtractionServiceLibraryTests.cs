using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Library-grounded extraction: relevant published-reference passages are retrieved (scoped to
/// the source's visibility) and threaded into the AI request. Party-visible sources read only
/// party-visible shelves; GM-only sources may also read GM-only shelves.
/// </summary>
[TestFixture]
public class ExtractionServiceLibraryTests
{
    private InMemorySourceRepository _sourceRepo = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeReferencePassageRetriever _retriever = null!;
    private ExtractionService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _aiClient = new FakeAiExtractionClient();
        _retriever = new FakeReferencePassageRetriever();

        var options = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 10.00m }
            }
        });

        _sut = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            new InMemoryReviewBatchRepository(),
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemoryAiUsageRecordRepository(),
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository(),
            new FakeBlobStorageService(),
            new FakePdfTextExtractor(),
            _aiClient,
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            new FakeMapExtractionClient(),
            new FakeAiBudgetGuard(),
            new FakeUnitOfWork(),
            options,
            NullLogger<ExtractionService>.Instance,
            _retriever);
    }

    private void SeedSource(VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        _sourceRepo.Seed(new Source
        {
            Id = SourceId,
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = "Captain Voss was spotted in Black Harbor.",
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedByUserId = UserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
    }

    private static KnowledgePassage Passage(string title = "Player's Handbook", int page = 42) => new()
    {
        ChunkId = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        DocumentTitle = title,
        Page = page,
        Text = "A ranger is a warden of the untamed wilds.",
        ReferenceId = "passage:x"
    };

    private static AiExtractionResponse SuccessResponse() => new()
    {
        Proposals =
        [
            new ExtractionProposal
            {
                ChangeType = "CreateArtifact",
                TargetType = "Artifact",
                ProposedValue = new { name = "Captain Voss", type = "Character", visibility = "PartyVisible" },
                Rationale = "Named in the source.",
                Confidence = 0.9m
            }
        ],
        InputTokens = 500,
        OutputTokens = 200,
        TotalTokens = 700,
        DurationMs = 1000,
        Model = "gpt-4o"
    };

    [Test]
    public async Task Extraction_ThreadsRetrievedPassagesIntoTheAiRequest()
    {
        SeedSource();
        _retriever.Passages.Add(Passage("Player's Handbook", 42));
        _aiClient.SetupSuccess(SuccessResponse());

        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        Assert.That(_aiClient.Requests[0].ReferencePassages, Has.Count.EqualTo(1));
        Assert.That(_aiClient.Requests[0].ReferencePassages[0].DocumentTitle, Is.EqualTo("Player's Handbook"));
    }

    [Test]
    public async Task Extraction_NoLibraryPassages_SendsEmptyReferenceList()
    {
        SeedSource();
        _aiClient.SetupSuccess(SuccessResponse());

        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(_aiClient.Requests[0].ReferencePassages, Is.Empty);
    }

    [Test]
    public async Task Extraction_PartyVisibleSource_RetrievesPartyVisibleShelvesOnly()
    {
        SeedSource(VisibilityScope.PartyVisible);
        _aiClient.SetupSuccess(SuccessResponse());

        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(_retriever.LastAllowedScopes, Is.EquivalentTo(new[] { VisibilityScope.PartyVisible }));
    }

    [Test]
    public async Task Extraction_GmOnlySource_RetrievesPartyVisibleAndGmOnlyShelves()
    {
        SeedSource(VisibilityScope.GMOnly);
        _aiClient.SetupSuccess(SuccessResponse());

        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(_retriever.LastAllowedScopes,
            Is.EquivalentTo(new[] { VisibilityScope.PartyVisible, VisibilityScope.GMOnly }));
    }

    [Test]
    public async Task Extraction_RetrievalQuery_IncludesSourceTitle_AndAttributesToSourceCreator()
    {
        SeedSource();
        _aiClient.SetupSuccess(SuccessResponse());

        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(_retriever.LastQuestion, Does.Contain("Session 5 Notes"));
        Assert.That(_retriever.LastAttributedUserId, Is.EqualTo(UserId));
    }
}
