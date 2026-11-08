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
/// Feature 43: triage sits between a validated response and the batch. What the model called
/// Incidental is never written; a response triage empties completes like one with nothing in it.
/// </summary>
[TestFixture]
public class ExtractionServiceTriageTests
{
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private ExtractionService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeAiExtractionClient();
        var artifactRepo = new InMemoryArtifactRepository();

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

        var usageRecorder = TestUsageRecorder.Wrap(_usageRepo);
        var budgetGuard = new FakeAiBudgetGuard();
        var attachmentRepository = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();
        var mapPipeline = new MapExtractionPipeline(
            attachmentRepository,
            new InMemoryMapPlacemarkRepository(),
            artifactRepo,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<MapExtractionPipeline>.Instance);
        var textDerivation = new SourceTextDerivation(
            _sourceRepo,
            attachmentRepository,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<SourceTextDerivation>.Instance);
        var handwritingTranscription = new HandwritingTranscriptionPipeline(
            _sourceRepo,
            attachmentRepository,
            blobStorage,
            new FakeHandwritingTranscriptionClient(),
            budgetGuard,
            usageRecorder,
            new HandwritingTranscriptionSettings(options.Value.AiModel, options.Value.AiTimeoutSeconds),
            NullLogger<HandwritingTranscriptionPipeline>.Instance);

        _sut = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            _proposalRepo,
            _sourceRefRepo,
            usageRecorder,
            artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _aiClient,
            mapPipeline,
            textDerivation,
            handwritingTranscription,
            budgetGuard,
            new FakeUnitOfWork(),
            options,
            NullLogger<ExtractionService>.Instance,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance);

        _sourceRepo.Seed(new Source
        {
            Id = SourceId,
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 12 Notes",
            Body = "A witch pulled Ugma aside and warned him that Koves and her son were unnatural.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
    }

    private static ExtractionProposal Proposal(string changeType, string targetType, object payload, ProposalSignificance significance) => new()
    {
        ChangeType = changeType,
        TargetType = targetType,
        ProposedValue = payload,
        Rationale = "From the session.",
        Confidence = 0.9m,
        Significance = significance
    };

    private static AiExtractionResponse Response(params ExtractionProposal[] proposals) => new()
    {
        Proposals = proposals,
        Usage = new AiUsage { InputTokens = 500, OutputTokens = 200, TotalTokens = 700, DurationMs = 900, Model = "gpt-4o" }
    };

    [Test]
    public async Task IncidentalProposals_AreNotWritten_TheRestAre()
    {
        _aiClient.SetupSuccess(Response(
            Proposal("CreateArtifact", "Artifact", new { name = "Koves", type = "Character", visibility = "PartyVisible" }, ProposalSignificance.Core),
            Proposal("AddFact", "ArtifactFact", new { artifactName = "Koves", predicate = "regarded as", value = "unnatural, by the witch", visibility = "PartyVisible" }, ProposalSignificance.Core),
            Proposal("CreateArtifact", "Artifact", new { name = "The spa's bath attendant", type = "Character", visibility = "PartyVisible" }, ProposalSignificance.Incidental),
            Proposal("AddFact", "ArtifactFact", new { artifactName = "Koves", predicate = "bath temperature preferred", value = "hot", visibility = "PartyVisible" }, ProposalSignificance.Incidental)));

        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
            Assert.That(outcome.ProposalCount, Is.EqualTo(2));
            Assert.That(_batchRepo.Batches.Single().Status, Is.EqualTo(ReviewBatchStatus.Pending));
            Assert.That(_proposalRepo.Proposals, Has.Count.EqualTo(2));
            Assert.That(_proposalRepo.Proposals.Select(p => p.ProposedValueJson), Has.None.Contains("bath"));
            Assert.That(_sourceRefRepo.References, Has.Count.EqualTo(2), "one citation per kept proposal, none for the dropped");
        });
    }

    [Test]
    public async Task AllIncidental_CompletesAnEmptyBatch_SourceProcessed_CallMetered()
    {
        _aiClient.SetupSuccess(Response(
            Proposal("CreateArtifact", "Artifact", new { name = "The spa's bath attendant", type = "Character", visibility = "PartyVisible" }, ProposalSignificance.Incidental),
            Proposal("CreateArtifact", "Artifact", new { name = "Hot bath etiquette", type = "Concept", visibility = "PartyVisible" }, ProposalSignificance.Incidental)));

        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
            Assert.That(outcome.ProposalCount, Is.Zero);
            Assert.That(_batchRepo.Batches.Single().Status, Is.EqualTo(ReviewBatchStatus.Completed));
            Assert.That(_proposalRepo.Proposals, Is.Empty);
            Assert.That(_sourceRepo.Sources.Single().ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
            Assert.That(_usageRepo.Records.Single().TotalTokens, Is.EqualTo(700), "the call was paid for whether or not anything survived");
        });
    }

    [Test]
    public async Task IncidentalCreate_AKeptFactNeeds_IsWritten()
    {
        _aiClient.SetupSuccess(Response(
            Proposal("CreateArtifact", "Artifact", new { name = "Grandmother Hespe", type = "Character", visibility = "PartyVisible" }, ProposalSignificance.Incidental),
            Proposal("AddFact", "ArtifactFact", new { artifactName = "Grandmother Hespe", predicate = "warned about", value = "Koves and her son", visibility = "PartyVisible" }, ProposalSignificance.Core)));

        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        Assert.That(_proposalRepo.Proposals, Has.Count.EqualTo(2));
    }
}
