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

[TestFixture]
public class ExtractionServiceTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private InMemoryReviewProposalRepository _reviewProposalRepository = null!;
    private InMemorySourceReferenceRepository _sourceReferenceRepository = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _artifactFactRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ExtractionOptions _options = null!;
    private ExtractionService _sut = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;

    private static readonly Guid CampaignId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _reviewProposalRepository = new InMemoryReviewProposalRepository();
        _sourceReferenceRepository = new InMemorySourceReferenceRepository();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _artifactFactRepository = new InMemoryArtifactFactRepository();
        _aiClient = new FakeAiExtractionClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _unitOfWork = new FakeUnitOfWork();

        _options = new ExtractionOptions
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
            _reviewBatchRepository,
            _reviewProposalRepository,
            _sourceReferenceRepository,
            _aiUsageRecordRepository,
            _artifactRepository,
            _artifactFactRepository,
            _aiClient,
            _budgetGuard, _unitOfWork,
            Options.Create(_options),
            NullLogger<ExtractionService>.Instance);
    }

    private Source CreateQueuedSource(string body = "Captain Voss was seen in Black Harbor.")
    {
        return new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
    }

    private static AiExtractionResponse CreateValidResponse(int proposalCount = 1)
    {
        var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
        {
            ChangeType = "CreateArtifact",
            TargetType = "Artifact",
            ProposedValue = new { name = $"Artifact {i}", type = "Character", visibility = "PartyVisible" },
            Rationale = $"Found in source text (proposal {i}).",
            Confidence = 0.85m
        }).ToList();

        return new AiExtractionResponse
        {
            Proposals = proposals,
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "gpt-4o"
        };
    }

    #region Daily AI budget gate

    [Test]
    public async Task ProcessExtractionAsync_BudgetExceeded_FailsSourceWithoutCallingAi()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _budgetGuard.Exceeded = true;

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_aiClient.CallCount, Is.EqualTo(0));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    #endregion

    #region Source Not Found → NonTransientFailure with SourceNotFound

    [Test]
    public async Task ProcessExtractionAsync_SourceNotFound_ReturnsNonTransientFailureWithSourceNotFound()
    {
        var nonExistentSourceId = Guid.NewGuid();

        var result = await _sut.ProcessExtractionAsync(nonExistentSourceId, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo(ErrorCategories.SourceNotFound));
    }

    [Test]
    public async Task ProcessExtractionAsync_SourceNotFound_DoesNotCreateAnyRecords()
    {
        var nonExistentSourceId = Guid.NewGuid();

        await _sut.ProcessExtractionAsync(nonExistentSourceId, CampaignId, CancellationToken.None);

        Assert.That(_reviewBatchRepository.Batches, Is.Empty);
        Assert.That(_reviewProposalRepository.Proposals, Is.Empty);
        Assert.That(_aiUsageRecordRepository.Records, Is.Empty);
    }

    #endregion

    #region Source in Processing/Processed/Failed status → Skipped outcome

    [Test]
    [TestCase(SourceProcessingStatus.Processing)]
    [TestCase(SourceProcessingStatus.Processed)]
    [TestCase(SourceProcessingStatus.Failed)]
    public async Task ProcessExtractionAsync_SourceNotQueued_ReturnsSkipped(SourceProcessingStatus status)
    {
        var source = CreateQueuedSource();
        source.ProcessingStatus = status;
        _sourceRepository.Seed(source);

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Skipped));
    }

    [Test]
    [TestCase(SourceProcessingStatus.Processing)]
    [TestCase(SourceProcessingStatus.Processed)]
    [TestCase(SourceProcessingStatus.Failed)]
    public async Task ProcessExtractionAsync_SourceNotQueued_DoesNotModifyStatus(SourceProcessingStatus status)
    {
        var source = CreateQueuedSource();
        source.ProcessingStatus = status;
        _sourceRepository.Seed(source);

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(status));
    }

    [Test]
    [TestCase(SourceProcessingStatus.Draft)]
    [TestCase(SourceProcessingStatus.Ready)]
    public async Task ProcessExtractionAsync_SourceInDraftOrReady_ReturnsSkipped(SourceProcessingStatus status)
    {
        var source = CreateQueuedSource();
        source.ProcessingStatus = status;
        _sourceRepository.Seed(source);

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Skipped));
    }

    #endregion

    #region Source with existing ReviewBatch in Pending/InReview/Completed → Skipped outcome

    [Test]
    [TestCase(ReviewBatchStatus.Pending)]
    [TestCase(ReviewBatchStatus.InReview)]
    [TestCase(ReviewBatchStatus.Completed)]
    public async Task ProcessExtractionAsync_ExistingReviewBatch_ReturnsSkipped(ReviewBatchStatus batchStatus)
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);

        var existingBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            SourceId = source.Id,
            Status = batchStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _reviewBatchRepository.CreateAsync(existingBatch);

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Skipped));
    }

    [Test]
    [TestCase(ReviewBatchStatus.Pending)]
    [TestCase(ReviewBatchStatus.InReview)]
    [TestCase(ReviewBatchStatus.Completed)]
    public async Task ProcessExtractionAsync_ExistingReviewBatch_DoesNotCreateNewBatch(ReviewBatchStatus batchStatus)
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);

        var existingBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            SourceId = source.Id,
            Status = batchStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _reviewBatchRepository.CreateAsync(existingBatch);

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Only the pre-existing batch should exist
        Assert.That(_reviewBatchRepository.Batches, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ProcessExtractionAsync_ExistingReviewBatchInCanceledStatus_DoesNotSkip()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);

        // Canceled or Failed batches should NOT prevent reprocessing
        var canceledBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Canceled,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _reviewBatchRepository.CreateAsync(canceledBatch);

        _aiClient.SetupSuccess(CreateValidResponse());

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Should not be skipped — Canceled batches don't block reprocessing
        Assert.That(result.Type, Is.Not.EqualTo(OutcomeType.Skipped));
    }

    #endregion

    #region Successful flow: Queued → Processing → Processed

    [Test]
    public async Task ProcessExtractionAsync_SuccessfulFlow_ReturnsSuccessOutcome()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupSuccess(CreateValidResponse(3));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.EqualTo(3));
        Assert.That(result.ReviewBatchId, Is.Not.Null);
    }

    [Test]
    public async Task ProcessExtractionAsync_SuccessfulFlow_TransitionsToProcessed()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupSuccess(CreateValidResponse());

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task ProcessExtractionAsync_SuccessfulFlow_TransitionsThroughProcessingFirst()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupSuccess(CreateValidResponse());

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Verify transitions recorded: Queued → Processing, then Processing → Processed
        var transitions = _sourceRepository.StatusTransitions
            .Where(t => t.SourceId == source.Id)
            .ToList();

        Assert.That(transitions, Has.Count.EqualTo(2));
        Assert.That(transitions[0].From, Is.EqualTo(SourceProcessingStatus.Queued));
        Assert.That(transitions[0].To, Is.EqualTo(SourceProcessingStatus.Processing));
        Assert.That(transitions[1].From, Is.EqualTo(SourceProcessingStatus.Processing));
        Assert.That(transitions[1].To, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task ProcessExtractionAsync_SuccessfulFlow_CreatesReviewBatch()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupSuccess(CreateValidResponse(2));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var batch = _reviewBatchRepository.Batches.Single();
        Assert.That(batch.SourceId, Is.EqualTo(source.Id));
        Assert.That(batch.CampaignId, Is.EqualTo(CampaignId));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));
        Assert.That(batch.Id, Is.EqualTo(result.ReviewBatchId));
    }

    #endregion

    #region Non-transient failure: Queued → Processing → Failed

    [Test]
    public async Task ProcessExtractionAsync_NonTransientFailure_ReturnsNonTransientOutcome()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupParseFailure();

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo(ErrorCategories.ParseFailure));
    }

    [Test]
    public async Task ProcessExtractionAsync_NonTransientFailure_TransitionsToFailed()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupParseFailure();

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task ProcessExtractionAsync_NonTransientFailure_TransitionsThroughProcessingFirst()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupParseFailure();

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var transitions = _sourceRepository.StatusTransitions
            .Where(t => t.SourceId == source.Id)
            .ToList();

        Assert.That(transitions, Has.Count.EqualTo(2));
        Assert.That(transitions[0].From, Is.EqualTo(SourceProcessingStatus.Queued));
        Assert.That(transitions[0].To, Is.EqualTo(SourceProcessingStatus.Processing));
        Assert.That(transitions[1].From, Is.EqualTo(SourceProcessingStatus.Processing));
        Assert.That(transitions[1].To, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task ProcessExtractionAsync_NonTransientFailure_DoesNotCreateReviewBatch()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupParseFailure();

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(_reviewBatchRepository.Batches, Is.Empty);
    }

    [Test]
    public async Task ProcessExtractionAsync_NonTransientException_TransitionsToFailed()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new InvalidOperationException("Unexpected AI failure"));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    #endregion

    #region Transient failure: source stays at Processing

    [Test]
    public async Task ProcessExtractionAsync_TransientTimeout_ReturnsTransientOutcome()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new TaskCanceledException("Operation timed out"));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo(ErrorCategories.Timeout));
    }

    [Test]
    public async Task ProcessExtractionAsync_TransientTimeout_SourceReturnsToQueued()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new TaskCanceledException("Operation timed out"));

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
    }

    [Test]
    public async Task ProcessExtractionAsync_TransientNetworkError_ReturnsTransientOutcome()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new HttpRequestException("Connection refused"));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo(ErrorCategories.TransientError));
    }

    [Test]
    public async Task ProcessExtractionAsync_TransientNetworkError_SourceReturnsToQueued()
    {
        // The abandoned message will be redelivered, and the idempotency check only
        // processes Queued sources — Processing would make every retry a silent no-op.
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new HttpRequestException("Connection refused"));

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
    }

    [Test]
    public async Task ProcessExtractionAsync_Http400_IsNonTransientAndFailsSource()
    {
        // A 4xx response means the request itself is rejected (e.g. an invalid response_format
        // schema) — retrying sends the same bytes. It must fail fast, not cycle redeliveries.
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new HttpRequestException(
            "AI call failed: HTTP 400", null, System.Net.HttpStatusCode.BadRequest));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task ProcessExtractionAsync_Http429_StaysTransient()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new HttpRequestException(
            "Transient AI service error: HTTP 429", null, System.Net.HttpStatusCode.TooManyRequests));

        var result = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
        var updated = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(updated!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
    }

    [Test]
    public async Task ProcessExtractionAsync_TransientFailure_DoesNotCreateReviewBatch()
    {
        var source = CreateQueuedSource();
        _sourceRepository.Seed(source);
        _aiClient.SetupTransientFailure(new TaskCanceledException("Operation timed out"));

        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        Assert.That(_reviewBatchRepository.Batches, Is.Empty);
    }

    #endregion
}
