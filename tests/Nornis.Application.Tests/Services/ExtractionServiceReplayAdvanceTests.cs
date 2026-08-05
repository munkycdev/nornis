using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The extraction pipeline is the other replay-advance trigger: a source whose extraction
/// yields NOTHING to review completes its batch on the spot, so the walk must be nudged
/// from the worker. A batch WITH proposals advances from the review pipeline instead —
/// extraction must not nudge for it.
/// </summary>
[TestFixture]
public class ExtractionServiceReplayAdvanceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeExtractionReplayAdvancer _advancer = null!;
    private ExtractionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _aiClient = new FakeAiExtractionClient();
        _advancer = new FakeExtractionReplayAdvancer();

        var options = new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        };

        var usageRecorder = TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository());
        var optionsWrapper = Options.Create(options);
        var budgetGuard = new FakeAiBudgetGuard();
        var artifactRepository = new InMemoryArtifactRepository();
        var attachmentRepository = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();
        var mapPipeline = new MapExtractionPipeline(
            attachmentRepository,
            new InMemoryMapPlacemarkRepository(),
            artifactRepository,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            optionsWrapper,
            NullLogger<MapExtractionPipeline>.Instance);
        var textDerivation = new SourceTextDerivation(
            _sourceRepository,
            attachmentRepository,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            optionsWrapper,
            NullLogger<SourceTextDerivation>.Instance);

        _sut = new ExtractionService(
            _sourceRepository,
            new InMemoryCampaignRepository(),
            new InMemoryReviewBatchRepository(),
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            usageRecorder,
            artifactRepository,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _aiClient,
            mapPipeline,
            textDerivation,
            budgetGuard,
            new FakeUnitOfWork(),
            optionsWrapper,
            NullLogger<ExtractionService>.Instance,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: _advancer);
    }

    private Source SeedQueuedSource(string body)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task ZeroProposalExtraction_NudgesTheAdvancer()
    {
        var source = SeedQueuedSource("Nothing of consequence happened.");
        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals = [],
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 200,
                TotalTokens = 700,
                DurationMs = 1200,
                Model = "gpt-4o"
            }
        });

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_advancer.Calls, Is.EqualTo([(WorldId, source.Id)]));
    }

    [Test]
    public async Task ExtractionWithProposals_LeavesTheNudgeToTheReviewPipeline()
    {
        var source = SeedQueuedSource("We questioned Captain Voss.");
        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Captain Voss", type = "Character", visibility = "PartyVisible" },
                    Rationale = "New character mentioned in session notes.",
                    Confidence = 0.85m
                }
            ],
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 200,
                TotalTokens = 700,
                DurationMs = 1200,
                Model = "gpt-4o"
            }
        });

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(Nornis.Application.Models.OutcomeType.Success));
        Assert.That(_advancer.Calls, Is.Empty);
    }

    [Test]
    public async Task EmptyBodySource_CompletesEmptyBatchAndNudgesTheAdvancer()
    {
        var source = SeedQueuedSource("   ");

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_advancer.Calls, Is.EqualTo([(WorldId, source.Id)]));
    }
}
