using Microsoft.Extensions.Options;
using NSubstitute;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LoremasterServiceUsageTrackingAndErrorHandlingTests
{
    private LoremasterService _service = null!;
    private FakeKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private LoremasterOptions _options = null!;

    private static readonly Guid CampaignId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

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
            MaxQuestionLength = 2000,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        };

        _service = new LoremasterService(
            _knowledgeRetriever,
            _aiClient,
            _aiUsageRecordRepository,
            new FakeAiBudgetGuard(), Options.Create(_options));
    }

    private AskLoremasterCommand CreateCommand(string question = "Who is Captain Voss?") =>
        new(CampaignId, question, UserId, CampaignRole.GM, null);

    private void SetupKnowledgeContext()
    {
        _knowledgeRetriever.NextContext = new KnowledgeContext
        {
            Artifacts = new List<KnowledgeArtifact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
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
    }

    #region Successful AI call creates AiUsageRecord with Succeeded=true

    [Test]
    public async Task AskAsync_SuccessfulAiCall_CreatesAiUsageRecordWithSucceededTrue()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Captain Voss is a sea captain based in Black Harbor.",
            InputTokens = 500,
            OutputTokens = 120,
            TotalTokens = 620,
            DurationMs = 800,
            Model = "gpt-4o"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.Succeeded, Is.True);
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasCorrectCampaignId()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess("Captain Voss is located in Black Harbor.");

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.CampaignId, Is.EqualTo(CampaignId));
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasCorrectUserId()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess("Captain Voss is located in Black Harbor.");

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.UserId, Is.EqualTo(UserId));
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasAskLoremasterOperationType()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess("Captain Voss is located in Black Harbor.");

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster));
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasCorrectModel()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Captain Voss is a sea captain.",
            InputTokens = 400,
            OutputTokens = 80,
            TotalTokens = 480,
            DurationMs = 500,
            Model = "gpt-4o"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.Model, Is.EqualTo("gpt-4o"));
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasCorrectTokenCounts()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Captain Voss is a sea captain.",
            InputTokens = 500,
            OutputTokens = 120,
            TotalTokens = 620,
            DurationMs = 800,
            Model = "gpt-4o"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.InputTokens, Is.EqualTo(500));
        Assert.That(record.OutputTokens, Is.EqualTo(120));
        Assert.That(record.TotalTokens, Is.EqualTo(620));
    }

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordHasNullErrorCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess("Captain Voss is located in Black Harbor.");

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.ErrorCode, Is.Null);
    }

    #endregion

    #region Failed AI call creates AiUsageRecord with Succeeded=false

    [Test]
    public async Task AskAsync_AiTimeout_CreatesAiUsageRecordWithSucceededFalse()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.Succeeded, Is.False);
    }

    [Test]
    public async Task AskAsync_AiTimeout_RecordHasTimeoutErrorCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.ErrorCode, Is.EqualTo("Timeout"));
    }

    [Test]
    public async Task AskAsync_AiRateLimit_CreatesAiUsageRecordWithSucceededFalse()
    {
        SetupKnowledgeContext();
        _aiClient.SetupRateLimited();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.Succeeded, Is.False);
    }

    [Test]
    public async Task AskAsync_AiRateLimit_RecordHasRateLimitedErrorCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupRateLimited();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.ErrorCode, Is.EqualTo("RateLimited"));
    }

    [Test]
    public async Task AskAsync_AiServiceError_CreatesAiUsageRecordWithSucceededFalse()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.Succeeded, Is.False);
    }

    [Test]
    public async Task AskAsync_AiServiceError_RecordHasServiceErrorCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.ErrorCode, Is.EqualTo("ServiceError"));
    }

    [Test]
    public async Task AskAsync_FailedAiCall_RecordStillHasCorrectCampaignAndUser()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.CampaignId, Is.EqualTo(CampaignId));
        Assert.That(record.UserId, Is.EqualTo(UserId));
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster));
    }

    #endregion

    #region AI timeout returns 503

    [Test]
    public async Task AskAsync_AiTimeout_Returns503Error()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public async Task AskAsync_AiTimeout_ReturnsServiceUnavailableCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("service_unavailable"));
    }

    [Test]
    public async Task AskAsync_AiTimeout_ReturnsUserFriendlyMessage()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Message,
            Is.EqualTo("The Loremaster is temporarily unavailable. Please try again."));
    }

    #endregion

    #region AI rate limit returns 429

    [Test]
    public async Task AskAsync_AiRateLimit_Returns429Error()
    {
        SetupKnowledgeContext();
        _aiClient.SetupRateLimited();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
    }

    [Test]
    public async Task AskAsync_AiRateLimit_ReturnsRateLimitedCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupRateLimited();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("rate_limited"));
    }

    [Test]
    public async Task AskAsync_AiRateLimit_ReturnsRetryMessage()
    {
        SetupKnowledgeContext();
        _aiClient.SetupRateLimited();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Message,
            Is.EqualTo("Too many requests. Please try again in a moment."));
    }

    #endregion

    #region AI service error returns 503

    [Test]
    public async Task AskAsync_AiServiceError_Returns503Error()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public async Task AskAsync_AiServiceError_ReturnsServiceUnavailableCode()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("service_unavailable"));
    }

    [Test]
    public async Task AskAsync_AiServiceError_ReturnsUserFriendlyMessage()
    {
        SetupKnowledgeContext();
        _aiClient.SetupServiceError();

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.Error!.Message,
            Is.EqualTo("The Loremaster is temporarily unavailable. Please try again."));
    }

    #endregion

    #region Cost calculation correctness

    [Test]
    public async Task AskAsync_SuccessfulCall_CalculatesCostCorrectly()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Captain Voss is a sea captain.",
            InputTokens = 1000,
            OutputTokens = 200,
            TotalTokens = 1200,
            DurationMs = 600,
            Model = "gpt-4o"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];

        // Expected: (1000 * 2.50 / 1_000_000) + (200 * 10.00 / 1_000_000)
        // = 0.0025 + 0.002 = 0.0045
        var expectedCost = (1000m * 2.50m / 1_000_000m) + (200m * 10.00m / 1_000_000m);
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(expectedCost));
    }

    [Test]
    public async Task AskAsync_SuccessfulCall_CostCalculation_WithLargeTokenCounts()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Detailed answer about the campaign.",
            InputTokens = 8000,
            OutputTokens = 2000,
            TotalTokens = 10000,
            DurationMs = 3000,
            Model = "gpt-4o"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];

        // Expected: (8000 * 2.50 / 1_000_000) + (2000 * 10.00 / 1_000_000)
        // = 0.02 + 0.02 = 0.04
        var expectedCost = (8000m * 2.50m / 1_000_000m) + (2000m * 10.00m / 1_000_000m);
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(expectedCost));
    }

    [Test]
    public async Task AskAsync_FailedCall_CostIsZero()
    {
        SetupKnowledgeContext();
        _aiClient.SetupTimeout();

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(0m));
    }

    [Test]
    public async Task AskAsync_UnknownModel_CostIsZero()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Answer text.",
            InputTokens = 500,
            OutputTokens = 100,
            TotalTokens = 600,
            DurationMs = 400,
            Model = "unknown-model"
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        var record = _aiUsageRecordRepository.Records[0];
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(0m));
    }

    #endregion

    #region Cancellation token passed through

    [Test]
    public async Task AskAsync_CancellationToken_PassedToAiClient()
    {
        SetupKnowledgeContext();
        using var cts = new CancellationTokenSource();

        var aiClient = Substitute.For<ILoremasterAiClient>();
        aiClient.AskAsync(Arg.Any<LoremasterAiRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LoremasterAiResponse
            {
                AnswerText = "Answer",
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                DurationMs = 200,
                Model = "gpt-4o"
            });

        var service = new LoremasterService(
            _knowledgeRetriever,
            aiClient,
            _aiUsageRecordRepository,
            new FakeAiBudgetGuard(), Options.Create(_options));

        await service.AskAsync(CreateCommand(), cts.Token);

        await aiClient.Received(1).AskAsync(
            Arg.Any<LoremasterAiRequest>(),
            cts.Token);
    }

    [Test]
    public async Task AskAsync_CancellationToken_PassedToKnowledgeRetriever()
    {
        using var cts = new CancellationTokenSource();

        var retriever = Substitute.For<IKnowledgeRetriever>();
        retriever.RetrieveAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CampaignRole>(),
                Arg.Any<CancellationToken>())
            .Returns(new KnowledgeContext
            {
                Artifacts = new List<KnowledgeArtifact>(),
                Facts = new List<KnowledgeFact>(),
                Relationships = new List<KnowledgeRelationship>(),
                SourceReferences = new List<KnowledgeSourceReference>()
            });

        _aiClient.SetupSuccess("Answer text");

        var service = new LoremasterService(
            retriever,
            _aiClient,
            _aiUsageRecordRepository,
            new FakeAiBudgetGuard(), Options.Create(_options));

        await service.AskAsync(CreateCommand(), cts.Token);

        await retriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            cts.Token);
    }

    #endregion
}
