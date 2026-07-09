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
/// Unit tests for ExtractionService usage tracking and cost calculation.
/// Validates Requirements 6.1–6.4.
/// </summary>
[TestFixture]
public class ExtractionServiceUsageTrackingTests
{
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeUnitOfWork _unitOfWork = null!;

    private const string DefaultModel = "gpt-4o";
    private const decimal DefaultInputRate = 2.50m;
    private const decimal DefaultOutputRate = 10.00m;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _aiClient = new FakeAiExtractionClient();
        _unitOfWork = new FakeUnitOfWork();
    }

    private ExtractionService CreateService(ExtractionOptions? options = null)
    {
        var opts = options ?? new ExtractionOptions
        {
            AiModel = DefaultModel,
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                [DefaultModel] = new ModelPricing
                {
                    InputPerMillionTokensUsd = DefaultInputRate,
                    OutputPerMillionTokensUsd = DefaultOutputRate
                }
            }
        };

        return new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            _proposalRepo,
            _sourceRefRepo,
            _usageRepo,
            _artifactRepo,
            _factRepo,
            _aiClient,
            new FakeAiBudgetGuard(), _unitOfWork,
            Options.Create(opts),
            NullLogger<ExtractionService>.Instance);
    }

    private static Source CreateQueuedSource(string body = "Captain Voss was seen in Black Harbor.")
    {
        return new Source
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Type = SourceType.SessionNote,
            Title = "Session 1 Notes",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CreatedByUserId = Guid.NewGuid()
        };
    }

    private static AiExtractionResponse CreateSuccessResponse(
        int inputTokens = 500,
        int outputTokens = 200,
        int totalTokens = 700,
        int durationMs = 1200,
        string model = DefaultModel)
    {
        return new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    TargetId = null,
                    ProposedValue = new { name = "Captain Voss", type = "Character", visibility = "PartyVisible" },
                    Rationale = "Captain Voss is mentioned in the source.",
                    Confidence = 0.9m
                }
            ],
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            DurationMs = durationMs,
            Model = model
        };
    }

    [Test]
    public async Task SuccessfulExtraction_CreatesAiUsageRecord_WithSucceededTrue()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        var response = CreateSuccessResponse(inputTokens: 1000, outputTokens: 400, totalTokens: 1400, durationMs: 2500);
        _aiClient.SetupSuccess(response);

        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.WorldId, Is.EqualTo(source.WorldId));
        Assert.That(record.SourceId, Is.EqualTo(source.Id));
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.SourceExtraction));
        Assert.That(record.Model, Is.EqualTo(DefaultModel));
        Assert.That(record.InputTokens, Is.EqualTo(1000));
        Assert.That(record.OutputTokens, Is.EqualTo(400));
        Assert.That(record.TotalTokens, Is.EqualTo(1400));
        Assert.That(record.DurationMs, Is.EqualTo(2500));
        Assert.That(record.Succeeded, Is.True);
        Assert.That(record.ErrorCode, Is.Null);
    }

    [Test]
    public async Task FailedExtraction_CreatesAiUsageRecord_WithSucceededFalseAndErrorCode()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        // Configure to throw a non-transient exception
        _aiClient.SetupTransientFailure(new InvalidOperationException("Unexpected AI failure"));

        var service = CreateService();

        // Act — this will be treated as a non-transient failure (generic exception)
        // Actually, let's use parse failure which is more predictable
        _aiClient.SetupParseFailure();

        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.WorldId, Is.EqualTo(source.WorldId));
        Assert.That(record.SourceId, Is.EqualTo(source.Id));
        Assert.That(record.Succeeded, Is.False);
        Assert.That(record.ErrorCode, Is.EqualTo(ErrorCategories.ParseFailure));
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.SourceExtraction));
    }

    [Test]
    public async Task CostCalculation_ComputesCorrectly_InputTimesRatePlusOutputTimesRate()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        const int inputTokens = 1500;
        const int outputTokens = 600;
        const decimal inputRate = 5.00m;
        const decimal outputRate = 15.00m;

        // Expected: (1500 * 5.00 / 1,000,000) + (600 * 15.00 / 1,000,000)
        //         = 0.0075 + 0.009 = 0.0165
        const decimal expectedCost = (inputTokens * inputRate / 1_000_000m) + (outputTokens * outputRate / 1_000_000m);

        var response = CreateSuccessResponse(inputTokens: inputTokens, outputTokens: outputTokens, totalTokens: inputTokens + outputTokens);
        _aiClient.SetupSuccess(response);

        var options = new ExtractionOptions
        {
            AiModel = DefaultModel,
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                [DefaultModel] = new ModelPricing
                {
                    InputPerMillionTokensUsd = inputRate,
                    OutputPerMillionTokensUsd = outputRate
                }
            }
        };

        var service = CreateService(options);

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(expectedCost));
    }

    [Test]
    public async Task FailedExtraction_TokenCountsZero_WhenResponseUnavailable()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        // Throw a timeout exception — response will be null for tracking
        _aiClient.SetupTransientFailure(new TaskCanceledException("Request timed out"));

        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.InputTokens, Is.EqualTo(0));
        Assert.That(record.OutputTokens, Is.EqualTo(0));
        Assert.That(record.TotalTokens, Is.EqualTo(0));
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(0m));
        Assert.That(record.Succeeded, Is.False);
    }

    [Test]
    public async Task SuccessfulExtraction_ReviewBatchIdSetOnAiUsageRecord()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        var response = CreateSuccessResponse();
        _aiClient.SetupSuccess(response);

        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(outcome.ReviewBatchId, Is.Not.Null);
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.ReviewBatchId, Is.EqualTo(outcome.ReviewBatchId));
    }

    [Test]
    public async Task FailedExtraction_ReviewBatchIdNull_WhenBatchNotCreated()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        // Parse failure means no batch is created
        _aiClient.SetupParseFailure();

        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.ReviewBatchId, Is.Null);
    }

    [Test]
    public async Task CostCalculation_ReturnsZero_WhenModelPricingNotConfigured()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        var response = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    TargetId = null,
                    ProposedValue = new { name = "Silver Key", type = "Item", visibility = "PartyVisible" },
                    Rationale = "Silver Key found in the source.",
                    Confidence = 0.85m
                }
            ],
            InputTokens = 1000,
            OutputTokens = 500,
            TotalTokens = 1500,
            DurationMs = 800,
            Model = "unknown-model"
        };
        _aiClient.SetupSuccess(response);

        // Model pricing only configured for "gpt-4o", not "unknown-model"
        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(0m),
            "Cost should be zero when model pricing is not configured for the returned model.");
    }

    [Test]
    public async Task TransientFailure_CreatesAiUsageRecord_WithTransientErrorCode()
    {
        // Arrange
        var source = CreateQueuedSource();
        _sourceRepo.Seed(source);

        // HttpRequestException with 503 is treated as transient
        _aiClient.SetupTransientFailure(new HttpRequestException("503 service unavailable"));

        var service = CreateService();

        // Act
        var outcome = await service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));

        var record = _usageRepo.Records[0];
        Assert.That(record.Succeeded, Is.False);
        Assert.That(record.ErrorCode, Is.EqualTo(ErrorCategories.TransientError));
        Assert.That(record.Model, Is.EqualTo(DefaultModel),
            "When response is unavailable, model should fall back to configured AiModel.");
    }
}
