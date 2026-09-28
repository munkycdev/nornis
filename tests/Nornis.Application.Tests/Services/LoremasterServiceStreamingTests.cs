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
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The answer as a stream: what the reader sees in what order, and what the usage ledger says
/// however the stream ended. The ledger rules are the point of most of these. A streamed answer
/// only learns its cost in its last chunk, so every way of not reaching that chunk is a way to
/// get an answer for free unless the service estimates it.
/// </summary>
[TestFixture]
public class LoremasterServiceStreamingTests
{
    private static readonly Guid WorldId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VossId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid HarborFactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private FakeKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;
    private InMemoryAiUsageRecordRepository _usageRepository = null!;
    private LoremasterOptions _options = null!;
    private LoremasterService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _knowledgeRetriever = new FakeKnowledgeRetriever
        {
            NextContext = new KnowledgeContext
            {
                Artifacts =
                [
                    new()
                    {
                        Id = VossId,
                        Name = "Captain Voss",
                        Type = "Character",
                        Summary = "A harbor captain with secrets",
                        ReferenceId = $"artifact:{VossId}"
                    }
                ],
                Facts =
                [
                    new()
                    {
                        Id = HarborFactId,
                        ArtifactId = VossId,
                        Predicate = "location",
                        Value = "Black Harbor",
                        TruthState = TruthState.Confirmed,
                        ReferenceId = $"fact:{HarborFactId}"
                    }
                ],
                Relationships = [],
                SourceReferences = []
            }
        };
        _aiClient = new FakeLoremasterAiClient();
        _usageRepository = new InMemoryAiUsageRecordRepository();
        _options = new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000,
        };

        _service = CreateService(_usageRepository);
    }

    private LoremasterService CreateService(IAiUsageRecordRepository usageRepository) =>
        new(_knowledgeRetriever, new FakeReferencePassageRetriever(), _aiClient,
            TestUsageRecorder.Wrap(usageRepository, loremaster: _options),
            new FakeAiBudgetGuard(), Options.Create(_options), NullLogger<LoremasterService>.Instance);

    private static AskLoremasterCommand Command() =>
        new(WorldId, "Where does Captain Voss keep his ledger?", UserId, WorldRole.GM, null);

    /// <summary>What the service estimates the prompt cost when the stream never said.</summary>
    private int EstimatedInputTokens()
    {
        var request = _aiClient.LastRequest!;
        return (request.SystemPrompt.Length + request.UserMessage.Length) / 4;
    }

    #region What the reader sees

    [Test]
    public async Task Stream_DeltasArriveInOrderBeforeCompleted_AndCitationsSplitAcrossChunksResolve()
    {
        // Both markers are cut mid-token, as a model's token stream cuts them. Citations can
        // only be parsed from the whole answer, never from a single delta.
        string[] chunks =
        [
            "Captain Voss sails out of Black Harbor [ref:arti",
            $"fact:{VossId}] and keeps his ledger aboard [ref:fa",
            $"ct:{HarborFactId}].",
        ];
        _aiClient.SetupChunks(chunks, usage: null);

        var begun = await _service.BeginAskAsync(Command(), CancellationToken.None);
        Assert.That(begun.IsSuccess, Is.True);

        var events = new List<LoremasterStreamEvent>();
        var streamWasOpenAtFirstDelta = false;
        await foreach (var e in begun.Value!.Events)
        {
            if (events.Count == 0)
            {
                // Proves the delta was forwarded as it arrived rather than after the whole
                // answer had been buffered: the model's stream is still open.
                streamWasOpenAtFirstDelta = !_aiClient.StreamDisposed;
            }

            events.Add(e);
        }

        Assert.That(streamWasOpenAtFirstDelta, Is.True, "the first delta waited for the whole answer");
        Assert.That(events.Take(3), Is.EqualTo(chunks.Select(c => new LoremasterStreamEvent.Delta(c))));
        Assert.That(events, Has.Count.EqualTo(4));

        var completed = events[3] as LoremasterStreamEvent.Completed;
        Assert.That(completed, Is.Not.Null, "the stream must end in exactly one Completed");
        Assert.Multiple(() =>
        {
            Assert.That(completed!.Answer.AnswerText, Is.EqualTo(string.Concat(chunks)));
            Assert.That(completed.Answer.Citations.Select(c => c.ReferenceId),
                Is.EqualTo([$"artifact:{VossId}", $"fact:{HarborFactId}"]));
            Assert.That(completed.Answer.Confidence, Is.EqualTo(begun.Value.Confidence));
            Assert.That(completed.Answer.Caveats, Is.EqualTo(begun.Value.Caveats));
        });
    }

    [Test]
    public async Task Stream_FailureAfterText_YieldsTheTextThenFailed()
    {
        _aiClient.SetupFailureAfter(
            ["Captain Voss sails ", "out of Black Harbor"],
            new AiHttpException("AI service unavailable: HTTP 503", 503));

        var events = await _service.CollectEventsAsync(Command(), CancellationToken.None);

        Assert.That(events, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(events[0], Is.EqualTo(new LoremasterStreamEvent.Delta("Captain Voss sails ")));
            Assert.That(events[1], Is.EqualTo(new LoremasterStreamEvent.Delta("out of Black Harbor")));
            var failed = events[2] as LoremasterStreamEvent.Failed;
            Assert.That(failed, Is.Not.Null, "a truncated answer must be marked as failed, not completed");
            Assert.That(failed!.Error.StatusCode, Is.EqualTo(503));
            Assert.That(failed.Error.Code, Is.EqualTo("service_unavailable"));
        });
    }

    #endregion

    #region What the ledger records

    [TestCase(503, "ServiceError")]
    [TestCase(429, "RateLimited")]
    public async Task Ledger_FailureAfterText_RecordsAFailedRowPricedByEstimate(int status, string errorCode)
    {
        const string delivered = "Captain Voss sails out of Black Harbor";
        _aiClient.SetupFailureAfter(
            ["Captain Voss sails ", "out of Black Harbor"],
            new AiHttpException($"HTTP {status}", status));

        await _service.CollectEventsAsync(Command(), CancellationToken.None);

        Assert.That(_usageRepository.Records, Has.Count.EqualTo(1));
        var record = _usageRepository.Records[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.False);
            Assert.That(record.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(record.OutputTokens, Is.EqualTo(delivered.Length / 4));
            Assert.That(record.InputTokens, Is.EqualTo(EstimatedInputTokens()).And.GreaterThan(0));
            Assert.That(record.TotalTokens, Is.EqualTo(record.InputTokens + record.OutputTokens));
        });
    }

    [Test]
    public async Task Ledger_FailureAfterTimeout_RecordsTimeoutPricedByEstimate()
    {
        _aiClient.SetupFailureAfter(
            ["Captain Voss sails ", "out of Black Harbor"],
            new AiTimeoutException("Loremaster AI call timed out after 30 seconds."));

        await _service.CollectEventsAsync(Command(), CancellationToken.None);

        var record = _usageRepository.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.False);
            Assert.That(record.ErrorCode, Is.EqualTo("Timeout"));
            Assert.That(record.OutputTokens, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Ledger_ClientThrowsOnTheCallItself_StillEndsInFailedAndARow()
    {
        // A client that throws from StreamAsync itself, not from its enumeration. The real client
        // is an iterator and cannot, but the guard must not depend on how a client is written.
        var throwing = Substitute.For<ILoremasterAiClient>();
        throwing.StreamAsync(Arg.Any<AiPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiHttpException("AI service unavailable: HTTP 503", 503));
        var service = new LoremasterService(
            _knowledgeRetriever, new FakeReferencePassageRetriever(), throwing,
            TestUsageRecorder.Wrap(_usageRepository, loremaster: _options),
            new FakeAiBudgetGuard(), Options.Create(_options), NullLogger<LoremasterService>.Instance);

        var events = await service.CollectEventsAsync(Command(), CancellationToken.None);

        Assert.That(events[^1], Is.TypeOf<LoremasterStreamEvent.Failed>());
        Assert.That(_usageRepository.Records, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_usageRepository.Records[0].Succeeded, Is.False);
            Assert.That(_usageRepository.Records[0].ErrorCode, Is.EqualTo("ServiceError"));
        });
    }

    [Test]
    public async Task Ledger_FailureBeforeText_RecordsAFailedRowAtZero()
    {
        _aiClient.SetupServiceError();

        await _service.CollectEventsAsync(Command(), CancellationToken.None);

        Assert.That(_usageRepository.Records, Has.Count.EqualTo(1));
        var record = _usageRepository.Records[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.False);
            Assert.That(record.ErrorCode, Is.EqualTo("ServiceError"));
            Assert.That(record.InputTokens, Is.Zero);
            Assert.That(record.OutputTokens, Is.Zero);
            Assert.That(record.TotalTokens, Is.Zero);
        });
    }

    [Test]
    public async Task Ledger_ReaderLeavesMidAnswer_RecordsACancelledSuccessPricedByEstimate()
    {
        const string firstChunk = "Captain Voss sails out of Black Harbor";
        _aiClient.SetupChunks([firstChunk, " and keeps his ledger aboard."], usage: null);

        var begun = await _service.BeginAskAsync(Command(), CancellationToken.None);
        await foreach (var e in begun.Value!.Events)
        {
            // The reader closes the page after the first words.
            break;
        }

        Assert.That(_aiClient.StreamDisposed, Is.True, "leaving must release the model's stream");
        Assert.That(_usageRepository.Records, Has.Count.EqualTo(1),
            "ask-then-leave must not be free");
        var record = _usageRepository.Records[0];
        Assert.Multiple(() =>
        {
            // Succeeded: the provider was answering. The outcome monitor reads this flag as
            // provider health, and a reader leaving says nothing about the provider.
            Assert.That(record.Succeeded, Is.True);
            Assert.That(record.ErrorCode, Is.EqualTo("Cancelled"));
            Assert.That(record.OutputTokens, Is.EqualTo(firstChunk.Length / 4));
            Assert.That(record.InputTokens, Is.EqualTo(EstimatedInputTokens()).And.GreaterThan(0));
        });
    }

    [Test]
    public async Task Ledger_ReaderCancelsMidAnswer_RowLandsDespiteTheCancelledToken()
    {
        // The HTTP abort path: the request token is cancelled rather than the enumerator
        // disposed. The row must be written with a token of its own, or a store that honours
        // cancellation (as EF does) would drop exactly the row this case exists to write.
        var written = new List<AiUsageRecord>();
        var repository = Substitute.For<IAiUsageRecordRepository>();
        repository.CreateAsync(Arg.Any<AiUsageRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                written.Add(call.Arg<AiUsageRecord>());
                return call.Arg<AiUsageRecord>();
            });
        var service = CreateService(repository);
        _aiClient.SetupChunks(["Captain Voss sails out of Black Harbor", " and keeps a ledger."], usage: null);

        using var cts = new CancellationTokenSource();
        var begun = await service.BeginAskAsync(Command(), cts.Token);

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in begun.Value!.Events)
            {
                await cts.CancelAsync();
            }
        });

        Assert.That(written, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(written[0].Succeeded, Is.True);
            Assert.That(written[0].ErrorCode, Is.EqualTo("Cancelled"));
            Assert.That(written[0].OutputTokens, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Ledger_ReaderCancelsBeforeAnyText_RecordsAZeroTokenCancelledSuccess()
    {
        // Every AI call leaves a row. Before the first word nothing is provably spent, so the
        // row is priced at zero; the reader leaving says nothing against the provider, so it
        // is a success for the outcome monitor.
        _aiClient.SetupHangUntilCancelled();

        using var cts = new CancellationTokenSource();
        var begun = await _service.BeginAskAsync(Command(), cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in begun.Value!.Events)
            {
                Assert.Fail($"no event should arrive from a model that never answered: {e}");
            }
        });

        Assert.That(_aiClient.CallCount, Is.EqualTo(1), "the model call must actually have started");
        Assert.That(_usageRepository.Records, Has.Count.EqualTo(1), "the attempt must still be on the ledger");
        var record = _usageRepository.Records[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.True);
            Assert.That(record.ErrorCode, Is.EqualTo("Cancelled"));
            Assert.That(record.WorldId, Is.EqualTo(WorldId));
            Assert.That(record.UserId, Is.EqualTo(UserId));
            Assert.That(record.InputTokens, Is.Zero);
            Assert.That(record.OutputTokens, Is.Zero);
            Assert.That(record.TotalTokens, Is.Zero);
        });
    }

    [Test]
    public async Task Ledger_CompletedWithoutReportedUsage_RecordsAnEstimateNotZero()
    {
        const string answer = "Captain Voss sails out of Black Harbor and keeps his ledger aboard.";
        _aiClient.SetupChunks(CannedLoremasterResponse.SplitIntoChunks(answer), usage: null);

        var result = await _service.AskAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var record = _usageRepository.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.Succeeded, Is.True);
            Assert.That(record.ErrorCode, Is.Null);
            Assert.That(record.Model, Is.EqualTo("gpt-4o"));
            Assert.That(record.OutputTokens, Is.EqualTo(answer.Length / 4));
            Assert.That(record.InputTokens, Is.EqualTo(EstimatedInputTokens()).And.GreaterThan(0));
            Assert.That(record.TotalTokens, Is.EqualTo(record.InputTokens + record.OutputTokens));
        });
    }

    [Test]
    public async Task Ledger_CompletedWithReportedUsage_RecordsTheReportedFigures()
    {
        _aiClient.SetupChunks(
            ["Captain Voss sails ", "out of Black Harbor."],
            new AiUsage { Model = "gpt-4o", InputTokens = 900, OutputTokens = 12, TotalTokens = 912, DurationMs = 0 });

        await _service.AskAsync(Command(), CancellationToken.None);

        var record = _usageRepository.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.InputTokens, Is.EqualTo(900));
            Assert.That(record.OutputTokens, Is.EqualTo(12));
            Assert.That(record.TotalTokens, Is.EqualTo(912));
        });
    }

    #endregion
}
