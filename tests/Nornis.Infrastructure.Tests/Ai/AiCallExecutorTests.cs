using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The shell every chat call goes through. The streaming half is new with streamed Ask, and its
/// hard cases are the ones a whole-response call never had: a failure after text has already
/// been handed out, and a timeout that has to cover a stream stalling halfway.
/// </summary>
[TestFixture]
public class AiCallExecutorTests
{
    private FakeChatClient _chatClient = null!;
    private FakeAiCallBinder _binder = null!;

    private static AiPromptRequest Request(int timeoutSeconds = 30) => new()
    {
        Scope = AiCallScope.ForWorld(Guid.NewGuid()),
        SystemPrompt = "You are the Loremaster.",
        UserMessage = "Who is Captain Voss?",
        Model = "gpt-4o",
        TimeoutSeconds = timeoutSeconds
    };

    [SetUp]
    public void SetUp()
    {
        _chatClient = new FakeChatClient();
        _binder = new FakeAiCallBinder(_chatClient);
    }

    private IAsyncEnumerable<AiStreamChunk> Stream(AiPromptRequest request, CancellationToken ct = default) =>
        AiCallExecutor.StreamAsync(
            _binder, AiTask.Ask, request, options: null, "Loremaster", NullLogger.Instance, ct);

    #region StreamAsync

    [Test]
    public async Task StreamAsync_YieldsTextInOrder_ThenOneUsageChunk()
    {
        _chatClient.Streams(
            ["Captain Voss ", "sails out of ", "Black Harbor."],
            new UsageDetails
            {
                InputTokenCount = 800,
                OutputTokenCount = 200,
                TotalTokenCount = 1000,
                CachedInputTokenCount = 300,
            });

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in Stream(Request()))
        {
            chunks.Add(chunk);
        }

        Assert.That(chunks.Take(3).Select(c => c.Text),
            Is.EqualTo(["Captain Voss ", "sails out of ", "Black Harbor."]));
        Assert.That(chunks.Take(3).Select(c => c.Usage), Is.All.Null,
            "usage belongs to the final chunk alone");
        Assert.That(chunks, Has.Count.EqualTo(4));

        var final = chunks[3];
        Assert.Multiple(() =>
        {
            Assert.That(final.Text, Is.Empty);
            Assert.That(final.Usage, Is.Not.Null);
            Assert.That(final.Usage!.Model, Is.EqualTo("gpt-4o"));
            Assert.That(final.Usage.InputTokens, Is.EqualTo(800));
            Assert.That(final.Usage.OutputTokens, Is.EqualTo(200));
            Assert.That(final.Usage.TotalTokens, Is.EqualTo(1000));
            Assert.That(final.Usage.CachedInputTokens, Is.EqualTo(300));
            Assert.That(final.Usage.DurationMs, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public async Task StreamAsync_ProviderReportedNoUsage_FinalChunkSaysNullNotZero()
    {
        // Zeros would read as a free call; the Loremaster estimates only when told "unknown".
        _chatClient.Streams(["Captain Voss."], usage: null);

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in Stream(Request()))
        {
            chunks.Add(chunk);
        }

        Assert.That(chunks, Has.Count.EqualTo(2));
        Assert.That(chunks[1].Text, Is.Empty);
        Assert.That(chunks[1].Usage, Is.Null);
    }

    [Test]
    public async Task StreamAsync_SendsTheSystemPromptThenTheUserMessage()
    {
        _chatClient.Streams(["An answer."]);

        await foreach (var _ in Stream(Request()))
        {
        }

        var (messages, _) = _chatClient.Calls.Single();
        Assert.That(messages.Select(m => (m.Role, m.Text)), Is.EqualTo(
        [
            (ChatRole.System, "You are the Loremaster."),
            (ChatRole.User, "Who is Captain Voss?"),
        ]));
    }

    [TestCase(429)]
    [TestCase(503)]
    public void StreamAsync_ClientResultExceptionMidStream_BecomesAiHttpExceptionWithItsStatus(int status)
    {
        _chatClient.Streams(["Captain Voss "], thenThrow: FakeChatClient.HttpFailure(status));

        var received = new List<string>();
        var ex = Assert.ThrowsAsync<AiHttpException>(async () =>
        {
            await foreach (var chunk in Stream(Request()))
            {
                received.Add(chunk.Text);
            }
        });

        Assert.That(received, Is.EqualTo(["Captain Voss "]), "text before the failure is still delivered");
        Assert.That(ex!.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public void StreamAsync_StallPastTheTimeout_BecomesAiTimeoutException()
    {
        // The timeout covers the whole answer, not the first token: this stream starts fine and
        // then stops talking.
        _chatClient.Streams(["Captain Voss "], thenHang: true);

        var received = new List<string>();
        var ex = Assert.ThrowsAsync<AiTimeoutException>(async () =>
        {
            await foreach (var chunk in Stream(Request(timeoutSeconds: 1)))
            {
                received.Add(chunk.Text);
            }
        });

        Assert.That(received, Is.EqualTo(["Captain Voss "]));
        Assert.That(ex!.Message, Does.Contain("timed out"));
    }

    [Test]
    public void StreamAsync_CallerCancelsMidStream_PropagatesAsOperationCanceled()
    {
        // A reader leaving is not a provider failure; it must not be dressed up as a timeout
        // or an HTTP error, both of which the Loremaster records as failed calls.
        _chatClient.Streams(["Captain Voss "], thenHang: true);
        using var cts = new CancellationTokenSource();

        var ex = Assert.CatchAsync(async () =>
        {
            await foreach (var _ in Stream(Request(), cts.Token))
            {
                await cts.CancelAsync();
            }
        });

        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
    }

    #endregion

    #region ExecuteAsync

    [Test]
    public async Task ExecuteAsync_ReturnsTheTextAndItsUsage()
    {
        _chatClient.Returns("{\"places\":[]}", input: 120, output: 8, total: 128);

        var (content, usage) = await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Extraction, Request(), options: null, "Test", NullLogger.Instance, CancellationToken.None);

        Assert.That(content, Is.EqualTo("{\"places\":[]}"));
        Assert.That((usage.InputTokens, usage.OutputTokens, usage.TotalTokens), Is.EqualTo((120, 8, 128)));
    }

    [Test]
    public void ExecuteAsync_EmptyCompletion_IsAnUnexpectedFailureNotAnEmptyString()
    {
        _chatClient.Returns(string.Empty);

        var ex = Assert.ThrowsAsync<AiHttpException>(() => AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Extraction, Request(), options: null, "Test", NullLogger.Instance, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.Null);
    }

    #endregion

    #region Binding

    [Test]
    public async Task ExecuteAsync_BindsTheRequestsScopeAndTask_BeforeCalling()
    {
        // Feature 39: who pays is decided per call, from the scope the service named.
        _chatClient.Returns("{}");
        var request = Request();

        await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Extraction, request, options: null, "Test", NullLogger.Instance, CancellationToken.None);

        Assert.That(_binder.Binds, Is.EqualTo([(request.Scope, AiTask.Extraction, "gpt-4o")]));
    }

    [Test]
    public async Task ExecuteAsync_ReportsTheBoundModel_NotTheRequestedOne()
    {
        // A user's key may run a different model from Nornis's; usage must price what ran.
        var binder = new RemappingBinder(_chatClient, "claude-sonnet-test");
        _chatClient.Returns("{}");

        var (_, usage) = await AiCallExecutor.ExecuteAsync(
            binder, AiTask.Ask, Request(), options: null, "Test", NullLogger.Instance, CancellationToken.None);

        Assert.That(usage.Model, Is.EqualTo("claude-sonnet-test"));
    }

    [Test]
    public async Task StreamAsync_ReportsTheBoundModel_NotTheRequestedOne()
    {
        var binder = new RemappingBinder(_chatClient, "claude-sonnet-test");
        _chatClient.Streams(["An answer."], new UsageDetails { InputTokenCount = 1, OutputTokenCount = 1, TotalTokenCount = 2 });

        AiUsage? usage = null;
        await foreach (var chunk in AiCallExecutor.StreamAsync(
            binder, AiTask.Ask, Request(), options: null, "Test", NullLogger.Instance, CancellationToken.None))
        {
            usage ??= chunk.Usage;
        }

        Assert.That(usage?.Model, Is.EqualTo("claude-sonnet-test"));
    }

    [Test]
    public void ExecuteAsync_BindingRefused_PropagatesWithoutCallingTheModel()
    {
        // A refusal (no key, trial spent) is decided before any token is spent.
        var binder = new ThrowingBinder(new AiHttpException("Refused.", 402));

        var ex = Assert.ThrowsAsync<AiHttpException>(() => AiCallExecutor.ExecuteAsync(
            binder, AiTask.Ask, Request(), options: null, "Test", NullLogger.Instance, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(402));
            Assert.That(_chatClient.Calls, Is.Empty);
        });
    }

    private sealed class RemappingBinder(FakeChatClient client, string model) : IAiCallBinder
    {
        public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct) =>
            Task.FromResult(new AiBinding(client, OpenAiProviderTraits.Instance,
                new AiFunding(Nornis.Domain.Enums.AiProvider.Anthropic, model, Nornis.Domain.Enums.AiFundedBy.Key)));
    }

    private sealed class ThrowingBinder(Exception exception) : IAiCallBinder
    {
        public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct) =>
            Task.FromException<AiBinding>(exception);
    }

    #endregion
}
