using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Regression: a worker restart mid-extraction leaves the source at Processing when
/// Service Bus redelivers the message. Skipping such messages (the old behavior)
/// wedged sources forever — no message left, no API path out of Processing. Observed
/// in production during the Symbaroum bulk import (2026-07-09) when a deploy landed
/// mid-run. A Processing source with no batch must be resumed; one WITH a batch
/// crashed after the commit and only needs its status repaired to Processed.
/// </summary>
[TestFixture]
public class ExtractionServiceCrashRecoveryTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private ExtractionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _aiClient = new FakeAiExtractionClient();

        var options = new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        };

        _sut = new ExtractionService(
            _sourceRepository,
            new InMemoryCampaignRepository(),
            _reviewBatchRepository,
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemoryAiUsageRecordRepository(),
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(),
            _aiClient,
            new FakeHandwritingTranscriptionClient(),
            new FakeAiBudgetGuard(),
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<ExtractionService>.Instance);

        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Captain Voss", type = "Character", visibility = "PartyVisible" },
                    Rationale = "Introduced in the source.",
                    Confidence = 0.9m
                }
            ],
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "gpt-4o"
        });
    }

    private Source SeedSource(SourceProcessingStatus status)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task ProcessingSource_WithNoBatch_IsResumedAndCompletes()
    {
        var source = SeedSource(SourceProcessingStatus.Processing);

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success),
            "a redelivered message for a crashed run must resume extraction, not skip");
        Assert.That(_aiClient.CallCount, Is.EqualTo(1));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task ProcessingSource_WithCompletedBatch_IsRepairedToProcessedWithoutReExtraction()
    {
        var source = SeedSource(SourceProcessingStatus.Processing);
        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped),
            "the batch already exists; only the status transition was lost in the crash");
        Assert.That(_aiClient.CallCount, Is.EqualTo(0), "no second AI call, no duplicate proposals");
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task QueuedSource_WithExistingBatch_StillSkipsWithoutStatusChange()
    {
        var source = SeedSource(SourceProcessingStatus.Queued);
        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.EqualTo(0));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued),
            "existing-batch skip must not touch the status of a non-Processing source");
    }
}
