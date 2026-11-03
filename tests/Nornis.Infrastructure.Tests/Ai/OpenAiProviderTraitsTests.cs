using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// How a provider-neutral <see cref="AiCallOptions"/> becomes the OpenAI SDK's own options
/// (feature 39). Moved here from the executor's old <c>JsonSchema</c> helper tests.
/// </summary>
[TestFixture]
public class OpenAiProviderTraitsTests
{
    private static readonly OpenAiProviderTraits Traits = OpenAiProviderTraits.Instance;

    private static readonly AiFunding NornisFunded = new(AiProvider.AzureOpenAi, "nornis-ask", AiFundedBy.Nornis);

    /// <summary>The SDK options the adapter will start from, and their response format as JSON.</summary>
    private static (ChatCompletionOptions Raw, JsonObject? Format) Raw(ChatOptions? options)
    {
        Assert.That(options, Is.Not.Null);
        var raw = options!.RawRepresentationFactory?.Invoke(new FakeChatClient()) as ChatCompletionOptions;
        Assert.That(raw, Is.Not.Null, "the options must travel as the SDK's own options object");
        var format = raw!.ResponseFormat is null
            ? null
            : JsonNode.Parse(ModelReaderWriter.Write(raw.ResponseFormat).ToString())!.AsObject();
        return (raw, format);
    }

    [Test]
    public void ChatOptions_NothingAsked_SendsNoOptionsAtAll()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Traits.ChatOptions(null), Is.Null);
            Assert.That(Traits.ChatOptions(new AiCallOptions()), Is.Null);
        });
    }

    [Test]
    public void ChatOptions_Schema_TravelsAsWrittenAndAlwaysStrict()
    {
        const string schema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""";

        var (raw, format) = Raw(Traits.ChatOptions(new AiCallOptions(new AiJsonSchema("map_places", schema))));

        var jsonSchema = format!["json_schema"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(format["type"]!.GetValue<string>(), Is.EqualTo("json_schema"));
            Assert.That(jsonSchema["name"]!.GetValue<string>(), Is.EqualTo("map_places"));
            Assert.That(jsonSchema["strict"]!.GetValue<bool>(), Is.True);
            Assert.That(jsonSchema["schema"]!.ToJsonString(), Is.EqualTo(schema));
            Assert.That(raw.MaxOutputTokenCount, Is.Null);
        });
    }

    [Test]
    public void ChatOptions_ExtractionIsStrictNow_WithItsClosedProposedValue()
    {
        // Until feature 39 extraction alone ran non-strict, because proposedValue was an open
        // object. Claude's and Gemini's strict modes refuse an open object, so it is closed with
        // every field nullable; the parser drops the nulls.
        var (_, format) = Raw(Traits.ChatOptions(ExtractionAiClient.CallOptions));

        var jsonSchema = format!["json_schema"]!.AsObject();
        var proposedValue = jsonSchema["schema"]!["properties"]!["proposals"]!["items"]!["properties"]!["proposedValue"]!;
        Assert.Multiple(() =>
        {
            Assert.That(jsonSchema["strict"]!.GetValue<bool>(), Is.True);
            Assert.That(proposedValue["additionalProperties"]!.GetValue<bool>(), Is.False);
            Assert.That(JsonNode.DeepEquals(
                    jsonSchema["schema"], JsonNode.Parse(ExtractionAiClient.GetStructuredOutputSchema())),
                Is.True, "the schema must reach the provider exactly as the client wrote it");
        });
    }

    [Test]
    public void ChatOptions_Ceiling_IsSetOnTheSdkOptions_NotTheAbstraction()
    {
        // On ChatOptions.MaxOutputTokens the adapter would translate it itself; the SDK's own
        // property is the path OutputCeilingWireTests proved reaches the wire as max_completion_tokens.
        var built = Traits.ChatOptions(new AiCallOptions(MaxOutputTokens: 2_500));
        var (raw, format) = Raw(built);

        Assert.Multiple(() =>
        {
            Assert.That(built!.MaxOutputTokens, Is.Null);
            Assert.That(built.Temperature, Is.Null);
            Assert.That(raw.MaxOutputTokenCount, Is.EqualTo(2_500));
            Assert.That(format, Is.Null);
        });
    }

    [Test]
    public void ChatOptions_BuildsFreshSdkOptionsForEveryCall()
    {
        // The SDK mutates the options object it sends; a shared instance would carry one call's
        // messages and model into the next.
        var options = Traits.ChatOptions(new AiCallOptions(new AiJsonSchema("map_places", """{"type":"object"}""")))!;

        var first = options.RawRepresentationFactory!(new FakeChatClient());
        var second = options.RawRepresentationFactory!(new FakeChatClient());

        Assert.That(first, Is.Not.SameAs(second));
    }

    [TestCase(400)]
    [TestCase(429)]
    public void TranslateFailure_SdkHttpFailure_KeepsItsStatus(int status)
    {
        var translated = Traits.TranslateFailure(FakeChatClient.HttpFailure(status), NornisFunded, "Test", NullLogger.Instance);

        Assert.That(translated?.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public void TranslateFailure_SomethingElse_IsNotThisProvidersToName()
    {
        Assert.That(Traits.TranslateFailure(new InvalidOperationException(), NornisFunded, "Test", NullLogger.Instance), Is.Null);
    }
}
