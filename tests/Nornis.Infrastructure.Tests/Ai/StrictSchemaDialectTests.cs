using System.Text.Json.Nodes;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Every structured-output schema a client sends, held to the one dialect OpenAI, Anthropic and
/// Gemini all accept in their strictest mode (feature 39): every object closed and every property
/// required, with optional values written as a <c>null</c> union. A schema that breaks it works
/// today on OpenAI and fails the day a user's key is a Claude or Gemini one. Passing here is not
/// enough for Claude: Anthropic also counts union and optional properties, which
/// <see cref="AnthropicSchemaLimitsTests"/> checks on the same list.
/// </summary>
[TestFixture]
public class StrictSchemaDialectTests
{
    /// <summary>Every structured-output schema a client sends; <see cref="AnthropicSchemaLimitsTests"/> holds the same list to Anthropic's counts.</summary>
    internal static IEnumerable<TestCaseData> Schemas()
    {
        yield return new TestCaseData(ArtifactSummaryAiClient.StructuredOutputSchema).SetName("artifact_summary");
        yield return new TestCaseData(AuditAiClient.GetStructuredOutputSchema()).SetName("continuity_findings");
        yield return new TestCaseData(CheckInAiClient.GetStructuredOutputSchema()).SetName("check_in_picks");
        yield return new TestCaseData(ContinuityFixAiClient.GetStructuredOutputSchema()).SetName("continuity_fix_proposals");
        yield return new TestCaseData(ConvergenceNarrationAiClient.StructuredOutputSchema).SetName("convergence_narration");
        yield return new TestCaseData(DigestAiClient.StructuredOutputSchema).SetName("world_digest");
        yield return new TestCaseData(ExtractionAiClient.GetStructuredOutputSchema()).SetName("extraction_proposals");
        yield return new TestCaseData(MapExtractionAiClient.GetStructuredOutputSchema()).SetName("map_places");
        yield return new TestCaseData(MapExtractionAiClient.GetRefinementOutputSchema()).SetName("map_place_positions");
        yield return new TestCaseData(RelationshipBackfillAiClient.GetStructuredOutputSchema()).SetName("backfill_links");
        yield return new TestCaseData(RetrospectiveAiClient.GetStructuredOutputSchema()).SetName("storyline_verdicts");
    }

    [TestCaseSource(nameof(Schemas))]
    public void Schema_IsClosedAndFullyRequired_Everywhere(string schema)
    {
        var breaches = new List<string>();
        Walk(JsonNode.Parse(schema)!, "$", breaches);

        Assert.That(breaches, Is.Empty);
    }

    [Test]
    public void TheRule_CatchesAnOpenObject_AndAnOptionalProperty()
    {
        // The rule's own test, so a walk that silently checks nothing can't pass the schemas.
        // This is the shape extraction's proposedValue had before feature 39.
        const string open = """
            {"type":"object","properties":{
              "a":{"type":"string"},
              "b":{"type":"object","properties":{},"additionalProperties":true}
            },"required":["a"],"additionalProperties":false}
            """;

        var breaches = new List<string>();
        Walk(JsonNode.Parse(open)!, "$", breaches);

        string[] expected =
        [
            "$: property 'b' is not required",
            "$.b: object is not closed",
        ];
        Assert.That(breaches, Is.EquivalentTo(expected));
    }

    private static void Walk(JsonNode node, string path, List<string> breaches)
    {
        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is { } item)
                {
                    Walk(item, $"{path}[{i}]", breaches);
                }
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        if (obj["properties"] is JsonObject properties)
        {
            if (obj["additionalProperties"]?.GetValue<bool>() is not false)
            {
                breaches.Add($"{path}: object is not closed");
            }

            var required = (obj["required"] as JsonArray)?.Select(r => r!.GetValue<string>()).ToHashSet() ?? [];
            foreach (var (name, _) in properties)
            {
                if (!required.Contains(name))
                {
                    breaches.Add($"{path}: property '{name}' is not required");
                }
            }

            foreach (var (name, child) in properties)
            {
                if (child is not null)
                {
                    Walk(child, $"{path}.{name}", breaches);
                }
            }
        }

        foreach (var key in new[] { "items", "anyOf", "oneOf", "$defs", "definitions" })
        {
            if (obj[key] is { } child)
            {
                Walk(child, $"{path}.{key}", breaches);
            }
        }
    }
}
