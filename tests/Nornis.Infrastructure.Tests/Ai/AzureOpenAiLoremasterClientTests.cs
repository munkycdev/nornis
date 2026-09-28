using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class AzureOpenAiLoremasterClientTests
{
    private FakeChatClient _chatClient = null!;
    private AzureOpenAiLoremasterClient _client = null!;

    private static readonly AiPromptRequest DefaultRequest = new()
    {
        SystemPrompt = "You are the Loremaster.",
        UserMessage = "Who is Captain Voss?",
        Model = "gpt-4o",
        TimeoutSeconds = 30
    };

    [SetUp]
    public void SetUp()
    {
        _chatClient = new FakeChatClient();
        _client = new AzureOpenAiLoremasterClient(
            _chatClient, NullLogger<AzureOpenAiLoremasterClient>.Instance);
    }

    private async Task<List<AiStreamChunk>> DrainAsync(
        AiPromptRequest? request = null,
        CancellationToken ct = default)
    {
        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in _client.StreamAsync(request ?? DefaultRequest, ct))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    #region Success Tests

    [Test]
    public async Task StreamAsync_ValidResponse_StreamsTheAnswerThenItsUsage()
    {
        _chatClient.Streams(
            ["Captain Voss is a shady harbor captain ", "based in Black Harbor. [ref:art-1]"],
            new UsageDetails { InputTokenCount = 800, OutputTokenCount = 200, TotalTokenCount = 1000 });

        var chunks = await DrainAsync();

        Assert.That(string.Concat(chunks.Select(c => c.Text)),
            Is.EqualTo("Captain Voss is a shady harbor captain based in Black Harbor. [ref:art-1]"));
        var usage = chunks[^1].Usage;
        Assert.That(usage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(usage!.InputTokens, Is.EqualTo(800));
            Assert.That(usage.OutputTokens, Is.EqualTo(200));
            Assert.That(usage.TotalTokens, Is.EqualTo(1000));
            Assert.That(usage.Model, Is.EqualTo("gpt-4o"));
            Assert.That(usage.DurationMs, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public async Task StreamAsync_SendsSystemPromptThenUserMessage_AndNoChatOptions()
    {
        _chatClient.Streams(["Some answer."]);

        await DrainAsync();

        var (messages, options) = _chatClient.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(messages.Select(m => m.Role), Is.EqualTo([ChatRole.System, ChatRole.User]));
            Assert.That(messages[0].Text, Is.EqualTo(DefaultRequest.SystemPrompt));
            Assert.That(messages[1].Text, Is.EqualTo(DefaultRequest.UserMessage));
            // No output ceiling, no temperature: see UnsupportedChatParameterTests.
            Assert.That(options, Is.Null);
        });
    }

    #endregion

    #region Timeout Tests

    [Test]
    public void StreamAsync_Timeout_ThrowsAiTimeoutException()
    {
        var shortTimeoutRequest = new AiPromptRequest
        {
            SystemPrompt = "System prompt",
            UserMessage = "Question?",
            Model = "gpt-4o",
            TimeoutSeconds = 1
        };
        _chatClient.Streams([], thenHang: true);

        var ex = Assert.ThrowsAsync<AiTimeoutException>(
            async () => await DrainAsync(shortTimeoutRequest));

        Assert.That(ex!.Message, Does.Contain("timed out"));
    }

    [Test]
    public void StreamAsync_ExternalCancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _chatClient.Streams([], thenHang: true);

        Assert.CatchAsync<OperationCanceledException>(
            async () => await DrainAsync(ct: cts.Token));
    }

    #endregion

    #region HTTP Failure Tests

    [TestCase(429)]
    [TestCase(500)]
    [TestCase(503)]
    [TestCase(400)]
    public void StreamAsync_HttpFailure_ThrowsAiHttpExceptionWithItsStatus(int status)
    {
        _chatClient.Streams([], thenThrow: FakeChatClient.HttpFailure(status));

        var ex = Assert.ThrowsAsync<AiHttpException>(async () => await DrainAsync());

        Assert.That(ex!.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public void StreamAsync_UnexpectedException_ThrowsAiHttpException()
    {
        _chatClient.Streams([], thenThrow: new InvalidOperationException("Something unexpected"));

        var ex = Assert.ThrowsAsync<AiHttpException>(async () => await DrainAsync());

        Assert.That(ex!.StatusCode, Is.Null);
        Assert.That(ex.Message, Does.Contain("Unexpected error"));
    }

    #endregion
}
