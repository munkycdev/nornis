using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Sources stored without extraction (ExtractionEnabled = false): "processing" files
/// them directly as Processed with no queue message, the extraction pipeline skips
/// them defensively, reprocessing re-files instead of re-queueing, and re-enabling
/// extraction surfaces the Process action again.
/// </summary>
[TestFixture]
[Category("Feature: source-extraction-opt-out")]
public class SourceExtractionOptOutTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private FakeExtractionQueueClient _queueClient = null!;
    private SourceService _sourceService = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _queueClient = new FakeExtractionQueueClient();
        _sourceService = new SourceService(_sourceRepo, new InMemoryWorldMemberRepository(),
            new InMemoryCampaignRepository(), _queueClient, _batchRepo,
            new InMemorySourceAttachmentRepository(), new FakeBlobStorageService(),
            NullLogger<SourceService>.Instance);
    }

    private Source SeedSource(
        SourceProcessingStatus status = SourceProcessingStatus.Draft,
        bool extractionEnabled = false)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.FanFiction,
            Title = "The Ballad of Captain Voss",
            Body = "A tale the players wrote between sessions.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            ExtractionEnabled = extractionEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = OwnerId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    [Test]
    public async Task MarkReady_ExtractionDisabled_StoresDirectlyWithoutQueueing()
    {
        var source = SeedSource();

        var result = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, OwnerId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(_queueClient.SentMessages, Is.Empty, "no extraction message is enqueued");
        Assert.That(_batchRepo.Batches, Is.Empty, "no review batch is created");
    }

    [Test]
    public async Task MarkReady_ExtractionDisabled_FromFailed_StoresDirectly()
    {
        var source = SeedSource(SourceProcessingStatus.Failed);

        var result = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, OwnerId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task Update_ReenablingExtraction_OnStoredSource_DropsToReady()
    {
        var source = SeedSource(SourceProcessingStatus.Processed);

        var result = await _sourceService.UpdateAsync(new UpdateSourceCommand(
            source.Id, WorldId, OwnerId, WorldRole.Player, ExtractionEnabled: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ExtractionEnabled, Is.True);
        Assert.That(result.Value.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Ready),
            "a stored source becomes processable again");
    }

    [Test]
    public async Task Update_ReenablingExtraction_OnExtractedSource_KeepsProcessed()
    {
        // A source that WAS extracted (batch exists) and later opted out: re-enabling
        // must not silently reset it — its knowledge is already in the record.
        var source = SeedSource(SourceProcessingStatus.Processed);
        await _batchRepo.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceId = source.Id,
            Status = ReviewBatchStatus.Completed, CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sourceService.UpdateAsync(new UpdateSourceCommand(
            source.Id, WorldId, OwnerId, WorldRole.Player, ExtractionEnabled: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task ExtractionService_SkipsOptedOutSource_AndFilesIt()
    {
        // Defense in depth: a queued message for a source whose flag was turned off
        // mid-flight must not extract.
        var source = SeedSource(SourceProcessingStatus.Queued);

        var extractionService = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemoryAiUsageRecordRepository(),
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(),
            new FakeAiExtractionClient(),
            new FakeHandwritingTranscriptionClient(),
            new FakeAiBudgetGuard(),
            new FakeUnitOfWork(),
            Options.Create(new ExtractionOptions
            {
                AiModel = "gpt-4o",
                AiEndpoint = "https://test.openai.azure.com/",
                MaxArtifactContextCount = 50,
                MaxFactsPerArtifact = 20,
                MaxParseRetryAttempts = 2
            }),
            NullLogger<ExtractionService>.Instance);

        var outcome = await extractionService.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        var updated = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(updated.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed),
            "the claimed source is filed rather than left wedged in the pipeline");
        Assert.That(_batchRepo.Batches, Is.Empty);
    }

    [Test]
    public async Task Reprocess_ExtractionDisabled_RefilesWithoutQueueing()
    {
        var source = SeedSource(SourceProcessingStatus.Processed);

        var reprocessService = new SourceReprocessService(
            _sourceRepo, _batchRepo, new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(), new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(), new InMemoryArtifactRelationshipRepository(),
            new InMemoryCharacterRepository(), _queueClient, new FakeUnitOfWork(),
            NullLogger<SourceReprocessService>.Instance);

        var result = await reprocessService.ReprocessAsync(new ReprocessSourceCommand(
            source.Id, WorldId, OwnerId, WorldRole.GM, Body: "A revised tale."), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Body, Is.EqualTo("A revised tale."));
        Assert.That(result.Value.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed),
            "re-filed, not re-queued");
        Assert.That(_queueClient.SentMessages, Is.Empty);
    }
}
