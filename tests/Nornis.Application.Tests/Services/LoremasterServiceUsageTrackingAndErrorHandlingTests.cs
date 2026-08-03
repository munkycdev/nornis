using Microsoft.Extensions.Logging.Abstractions;
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

[TestFixture]
public class LoremasterServiceUsageTrackingAndErrorHandlingTests
{
    private LoremasterService _service = null!;
    private FakeKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private LoremasterOptions _options = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
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
            _knowledgeRetriever, new FakeReferencePassageRetriever(),
            _aiClient,
            TestUsageRecorder.Wrap(_aiUsageRecordRepository, loremaster: _options),
            new FakeAiBudgetGuard(), Options.Create(_options), NullLogger<LoremasterService>.Instance);
    }

    private AskLoremasterCommand CreateCommand(string question = "Who is Captain Voss?") =>
        new(WorldId, question, UserId, WorldRole.GM, null);

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

    /// <summary>
    /// The three ways the AI client fails, and the only axis the failure tests vary.
    /// </summary>
    public enum AiFailure
    {
        Timeout,
        RateLimited,
        ServiceError
    }

    private void SetupFailure(AiFailure failure)
    {
        switch (failure)
        {
            case AiFailure.Timeout:
                _aiClient.SetupTimeout();
                break;
            case AiFailure.RateLimited:
                _aiClient.SetupRateLimited();
                break;
            default:
                _aiClient.SetupServiceError();
                break;
        }
    }

    #region Successful AI call creates AiUsageRecord with Succeeded=true

    [Test]
    public async Task AskAsync_SuccessfulAiCall_RecordsTheWholeCall()
    {
        SetupKnowledgeContext();
        _aiClient.SetupSuccess(new LoremasterAiResponse
        {
            AnswerText = "Captain Voss is a sea captain based in Black Harbor.",
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 120,
                TotalTokens = 620,
                DurationMs = 800,
                Model = "gpt-4o"
            }
        });

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];

        // Every field of the record at once. Billing reads all of them together, so a
        // gap in any one is the same defect — an unattributable charge.
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.True);
            Assert.That(record.WorldId, Is.EqualTo(WorldId));
            Assert.That(record.UserId, Is.EqualTo(UserId));
            Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster));
            Assert.That(record.Model, Is.EqualTo("gpt-4o"));
            Assert.That(record.InputTokens, Is.EqualTo(500));
            Assert.That(record.OutputTokens, Is.EqualTo(120));
            Assert.That(record.TotalTokens, Is.EqualTo(620));
            Assert.That(record.ErrorCode, Is.Null);
        });
    }

    #endregion

    #region Failed AI call creates AiUsageRecord with Succeeded=false

    [TestCase(AiFailure.Timeout, "Timeout")]
    [TestCase(AiFailure.RateLimited, "RateLimited")]
    [TestCase(AiFailure.ServiceError, "ServiceError")]
    public async Task AskAsync_FailedAiCall_StillRecordsTheAttempt(AiFailure failure, string errorCode)
    {
        SetupKnowledgeContext();
        SetupFailure(failure);

        await _service.AskAsync(CreateCommand(), CancellationToken.None);

        Assert.That(_aiUsageRecordRepository.Records, Has.Count.EqualTo(1));
        var record = _aiUsageRecordRepository.Records[0];

        // A failed call still cost tokens upstream, and it still has to be attributable
        // to a world and a user — the attribution fields are asserted here, not only on
        // the success path, because that is where they would quietly go missing.
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.False);
            Assert.That(record.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(record.WorldId, Is.EqualTo(WorldId));
            Assert.That(record.UserId, Is.EqualTo(UserId));
            Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster));
        });
    }

    #endregion

    #region AI failures map to HTTP status, code and message

    [TestCase(AiFailure.Timeout, 503, "service_unavailable",
        "The Loremaster is temporarily unavailable. Please try again.")]
    [TestCase(AiFailure.RateLimited, 429, "rate_limited",
        "Too many requests. Please try again in a moment.")]
    [TestCase(AiFailure.ServiceError, 503, "service_unavailable",
        "The Loremaster is temporarily unavailable. Please try again.")]
    public async Task AskAsync_AiFailure_ReturnsTheMappedError(
        AiFailure failure, int status, string code, string message)
    {
        SetupKnowledgeContext();
        SetupFailure(failure);

        var result = await _service.AskAsync(CreateCommand(), CancellationToken.None);

        // Timeout and service error deliberately land on the same triple: the caller
        // cannot act differently on them, and naming the distinction would leak which
        // upstream failed.
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(status));
            Assert.That(result.Error!.Code, Is.EqualTo(code));
            Assert.That(result.Error!.Message, Is.EqualTo(message));
        });
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
            Usage = new AiUsage
            {
                InputTokens = 1000,
                OutputTokens = 200,
                TotalTokens = 1200,
                DurationMs = 600,
                Model = "gpt-4o"
            }
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
            AnswerText = "Detailed answer about the world.",
            Usage = new AiUsage
            {
                InputTokens = 8000,
                OutputTokens = 2000,
                TotalTokens = 10000,
                DurationMs = 3000,
                Model = "gpt-4o"
            }
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
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 100,
                TotalTokens = 600,
                DurationMs = 400,
                Model = "unknown-model"
            }
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
        aiClient.AskAsync(Arg.Any<AiPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LoremasterAiResponse
            {
                AnswerText = "Answer",
                Usage = new AiUsage
                {
                    InputTokens = 100,
                    OutputTokens = 50,
                    TotalTokens = 150,
                    DurationMs = 200,
                    Model = "gpt-4o"
                }
            });

        var service = new LoremasterService(
            _knowledgeRetriever, new FakeReferencePassageRetriever(),
            aiClient,
            TestUsageRecorder.Wrap(_aiUsageRecordRepository, loremaster: _options),
            new FakeAiBudgetGuard(), Options.Create(_options), NullLogger<LoremasterService>.Instance);

        await service.AskAsync(CreateCommand(), cts.Token);

        await aiClient.Received(1).AskAsync(
            Arg.Any<AiPromptRequest>(),
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
                Arg.Any<WorldRole>(),
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
            retriever, new FakeReferencePassageRetriever(),
            _aiClient,
            TestUsageRecorder.Wrap(_aiUsageRecordRepository, loremaster: _options),
            new FakeAiBudgetGuard(), Options.Create(_options), NullLogger<LoremasterService>.Instance);

        await service.AskAsync(CreateCommand(), cts.Token);

        await retriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<WorldRole>(),
            cts.Token);
    }

    #endregion
}
