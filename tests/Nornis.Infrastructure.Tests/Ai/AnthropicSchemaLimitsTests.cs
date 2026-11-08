using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Anthropic's structured outputs refuse a schema with more than 16 union-typed properties or
/// more than 24 optional ones, with an HTTP 400 before any work. Extraction's schema had 18
/// nullable fields, and every extraction on a Claude key failed that way in production
/// (2026-11-03 to 2026-11-06) while <see cref="StrictSchemaDialectTests"/> was green, because
/// the strict dialect says nothing about counts.
/// </summary>
[TestFixture]
public class AnthropicSchemaLimitsTests
{
    [TestCaseSource(typeof(StrictSchemaDialectTests), nameof(StrictSchemaDialectTests.Schemas))]
    public void Every_schema_as_Anthropic_receives_it_is_within_its_limits(string schema)
    {
        var counts = AnthropicSchemaLimits.Count(SentToAnthropic(schema));

        Assert.That(counts.WithinLimits, Is.True,
            $"{counts.UnionProperties} union properties (max {AnthropicSchemaLimits.MaxUnionProperties}), " +
            $"{counts.OptionalProperties} optional (max {AnthropicSchemaLimits.MaxOptionalProperties})");
    }

    [TestCaseSource(typeof(StrictSchemaDialectTests), nameof(StrictSchemaDialectTests.Schemas))]
    public void A_schema_Anthropic_accepts_as_written_reaches_it_unchanged(string schema)
    {
        var written = JsonNode.Parse(schema)!;
        if (AnthropicSchemaLimits.Count(written).UnionProperties > AnthropicSchemaLimits.MaxUnionProperties)
        {
            Assert.Ignore("Over the union limit as written, so it is fitted; covered below.");
        }

        Assert.That(JsonNode.DeepEquals(SentToAnthropic(schema), written), Is.True);
    }

    [Test]
    public void The_extraction_schema_is_over_the_limit_as_written()
    {
        // Why the fit exists. If this ever goes green-by-ignore above, the fit is dead code.
        var counts = AnthropicSchemaLimits.Count(JsonNode.Parse(ExtractionAiClient.GetStructuredOutputSchema())!);

        Assert.That(counts.UnionProperties, Is.GreaterThan(AnthropicSchemaLimits.MaxUnionProperties));
    }

    [Test]
    public void Fitting_turns_each_nullable_into_an_optional_property_of_its_own_type()
    {
        var fitted = AnthropicSchemaLimits.Fit(JsonNode.Parse(ExtractionAiClient.GetStructuredOutputSchema())!);

        var proposal = fitted["properties"]!["proposals"]!["items"]!;
        var payload = proposal["properties"]!["proposedValue"]!;
        string[] proposalRequired = [.. proposal["required"]!.AsArray().Select(r => r!.GetValue<string>())];
        Assert.Multiple(() =>
        {
            Assert.That(proposal["properties"]!["targetId"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
            Assert.That(proposal["properties"]!["quote"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
            Assert.That(payload["properties"]!["confidence"]!["type"]!.GetValue<string>(), Is.EqualTo("number"));
            Assert.That(proposalRequired,
                Is.EquivalentTo(["changeType", "targetType", "proposedValue", "rationale", "confidence", "significance"]));
            Assert.That(payload["required"]!.AsArray(), Is.Empty);
            Assert.That(payload["additionalProperties"]!.GetValue<bool>(), Is.False);
            Assert.That(AnthropicSchemaLimits.Count(fitted).UnionProperties, Is.Zero);
        });
    }

    [Test]
    public void Fitting_leaves_the_callers_schema_alone()
    {
        var written = JsonNode.Parse(ExtractionAiClient.GetStructuredOutputSchema())!;
        var before = written.ToJsonString();

        AnthropicSchemaLimits.Fit(written);

        Assert.That(written.ToJsonString(), Is.EqualTo(before));
    }

    [Test]
    public void The_count_sees_type_arrays_anyOf_and_unrequired_properties_at_any_depth()
    {
        // The rule's own test, so a count that silently finds nothing can't pass the schemas.
        const string schema = """
            {"type":"object","properties":{
              "a":{"type":"string"},
              "b":{"type":["string","null"]},
              "c":{"anyOf":[{"type":"string"},{"type":"integer"}]},
              "list":{"type":"array","items":{"type":"object","properties":{
                "d":{"type":["number","null"]},
                "e":{"type":"string"}
              },"required":["d"],"additionalProperties":false}}
            },"required":["a","b","list"],"additionalProperties":false}
            """;

        var counts = AnthropicSchemaLimits.Count(JsonNode.Parse(schema)!);

        Assert.Multiple(() =>
        {
            Assert.That(counts.UnionProperties, Is.EqualTo(3), "b, c and list.d");
            Assert.That(counts.OptionalProperties, Is.EqualTo(2), "c and list.e");
        });
    }

    /// <summary>The schema exactly as the Anthropic traits hand it to the SDK's adapter.</summary>
    private static JsonNode SentToAnthropic(string schema)
    {
        var options = AnthropicProviderTraits.Instance.ChatOptions(new AiCallOptions(new AiJsonSchema("s", schema)));
        var format = (ChatResponseFormatJson)options!.ResponseFormat!;
        return JsonNode.Parse(format.Schema!.Value.GetRawText())!;
    }
}
