using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The shell every chat call goes through. The streaming half is new with streamed Ask, and its
/// hard cases are the ones a whole-response call never had: a failure after text has already
/// been handed out, and a timeout that has to cover a stream stalling halfway.
/// </summary>
[TestFixture]
public class AzureOpenAiCallExecutorTests
{
    private FakeChatClient _chatClient = null!;

    private static AiPromptRequest Request(int timeoutSeconds = 30) => new()
    {
        SystemPrompt = "You are the Loremaster.",
        UserMessage = "Who is Captain Voss?",
        Model = "gpt-4o",
        TimeoutSeconds = timeoutSeconds
    };

    [SetUp]
    public void SetUp() => _chatClient = new FakeChatClient();

    private IAsyncEnumerable<AiStreamChunk> Stream(AiPromptRequest request, CancellationToken ct = default) =>
        AzureOpenAiCallExecutor.StreamAsync(
            _chatClient, request, chatOptions: null, "Loremaster", NullLogger.Instance, ct);

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

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, Request(), chatOptions: null, "Test", NullLogger.Instance, CancellationToken.None);

        Assert.That(content, Is.EqualTo("{\"places\":[]}"));
        Assert.That((usage.InputTokens, usage.OutputTokens, usage.TotalTokens), Is.EqualTo((120, 8, 128)));
    }

    [Test]
    public void ExecuteAsync_EmptyCompletion_IsAnUnexpectedFailureNotAnEmptyString()
    {
        _chatClient.Returns(string.Empty);

        var ex = Assert.ThrowsAsync<AiHttpException>(() => AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, Request(), chatOptions: null, "Test", NullLogger.Instance, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.Null);
    }

    #endregion

    #region JsonSchema

    /// <summary>The SDK options the adapter will start from, and their response format as JSON.</summary>
    private static (ChatCompletionOptions Raw, JsonObject Format) RawResponseFormat(ChatOptions options)
    {
        var raw = options.RawRepresentationFactory?.Invoke(new FakeChatClient()) as ChatCompletionOptions;
        Assert.That(raw, Is.Not.Null, "the schema must travel as the SDK's own options object");
        var format = JsonNode.Parse(ModelReaderWriter.Write(raw!.ResponseFormat).ToString())!.AsObject();
        return (raw, format);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void JsonSchema_CarriesTheSchemaAsWrittenAndTheRequestedStrictness(bool strict)
    {
        const string schema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""";

        var (_, format) = RawResponseFormat(AzureOpenAiCallExecutor.JsonSchema("map_places", schema, strict));

        var jsonSchema = format["json_schema"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(format["type"]!.GetValue<string>(), Is.EqualTo("json_schema"));
            Assert.That(jsonSchema["name"]!.GetValue<string>(), Is.EqualTo("map_places"));
            Assert.That(jsonSchema["strict"]!.GetValue<bool>(), Is.EqualTo(strict));
            Assert.That(jsonSchema["schema"]!.ToJsonString(), Is.EqualTo(schema));
        });
    }

    [Test]
    public void JsonSchema_ExtractionStaysNonStrict_WithItsOpenProposedValue()
    {
        // The reason JsonSchema exists: the adapter's own schema path forces strict mode's
        // transform on every schema, which would close proposedValue and leave the model able
        // to fill it with nothing but {}.
        var (_, format) = RawResponseFormat(AzureOpenAiExtractionClient.BuildCompletionOptions());

        var jsonSchema = format["json_schema"]!.AsObject();
        var proposedValue = jsonSchema["schema"]!["properties"]!["proposals"]!["items"]!["properties"]!["proposedValue"]!;
        Assert.Multiple(() =>
        {
            Assert.That(jsonSchema["strict"]!.GetValue<bool>(), Is.False);
            Assert.That(proposedValue["additionalProperties"]!.GetValue<bool>(), Is.True);
            Assert.That(JsonNode.DeepEquals(
                    jsonSchema["schema"], JsonNode.Parse(AzureOpenAiExtractionClient.GetStructuredOutputSchema())),
                Is.True, "the schema must reach the provider exactly as the client wrote it");
        });
    }

    [Test]
    public void JsonSchema_BuildsFreshSdkOptionsForEveryCall()
    {
        // The SDK mutates the options object it sends; a shared instance would carry one call's
        // messages and model into the next.
        var options = AzureOpenAiCallExecutor.JsonSchema("map_places", """{"type":"object"}""", strict: true);

        var first = options.RawRepresentationFactory!(new FakeChatClient());
        var second = options.RawRepresentationFactory!(new FakeChatClient());

        Assert.That(first, Is.Not.SameAs(second));
    }

    #endregion
}
