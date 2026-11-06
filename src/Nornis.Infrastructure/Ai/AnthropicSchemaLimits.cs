using System.Text.Json.Nodes;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The schema-complexity limits Anthropic's structured outputs enforce on top of the strict
/// dialect (<see cref="AiJsonSchema"/>), and the one rewrite that brings a schema under them.
///
/// Anthropic counts, across every strict schema in a request, the properties whose schema is a
/// union (a <c>type</c> array such as <c>["string","null"]</c>, or an <c>anyOf</c>) and allows
/// 16; and the properties left out of their object's <c>required</c>, allowing 24. Past either
/// it answers HTTP 400 before generating anything. The strict dialect writes every optional value
/// as a null union, so a schema with many optional fields breaks the first limit while the
/// second stays empty: extraction's has 18, and every extraction on a Claude key failed from the
/// day Claude keys went live (2026-11-03) until this existed.
///
/// The rewrite trades unions for optional properties: <c>"x": {"type": ["string","null"]}</c>
/// required becomes <c>"x": {"type": "string"}</c> not required. The model then leaves a field
/// out where it would have sent null, and every parser here already reads a missing property
/// as null. It runs only when a schema is over the union limit, so a schema Anthropic accepts as
/// written reaches it byte for byte as OpenAI and Gemini see it.
/// </summary>
internal static class AnthropicSchemaLimits
{
    public const int MaxUnionProperties = 16;

    public const int MaxOptionalProperties = 24;

    public readonly record struct Counts(int UnionProperties, int OptionalProperties)
    {
        public bool WithinLimits =>
            UnionProperties <= MaxUnionProperties && OptionalProperties <= MaxOptionalProperties;
    }

    /// <summary>How the schema counts against Anthropic's limits.</summary>
    public static Counts Count(JsonNode schema)
    {
        var unions = 0;
        var optional = 0;
        foreach (var (property, required) in Properties(schema))
        {
            if (property["type"] is JsonArray || property["anyOf"] is not null)
            {
                unions++;
            }

            if (!required)
            {
                optional++;
            }
        }

        return new Counts(unions, optional);
    }

    /// <summary>
    /// The schema unchanged when Anthropic accepts it as written; otherwise a copy with every
    /// <c>[T, "null"]</c> property turned into an optional <c>T</c>. The result can still be over
    /// a limit (more than 24 nullable fields); <c>AnthropicSchemaLimitsTests</c> holds every schema
    /// a client sends to fitting.
    /// </summary>
    public static JsonNode Fit(JsonNode schema)
    {
        if (Count(schema).UnionProperties <= MaxUnionProperties)
        {
            return schema;
        }

        var fitted = schema.DeepClone();
        MakeNullablesOptional(fitted);
        return fitted;
    }

    private static void MakeNullablesOptional(JsonNode node)
    {
        foreach (var child in Children(node))
        {
            MakeNullablesOptional(child);
        }

        if (node is not JsonObject obj || obj["properties"] is not JsonObject properties)
        {
            return;
        }

        var required = obj["required"] as JsonArray;
        foreach (var (name, property) in properties)
        {
            if (property is JsonObject propertyObject && NonNullType(propertyObject) is { } type)
            {
                propertyObject["type"] = type;
                if (required?.FirstOrDefault(r => r?.GetValue<string>() == name) is { } entry)
                {
                    required.Remove(entry);
                }
            }
        }
    }

    /// <summary>The <c>T</c> of a <c>"type": [T, "null"]</c>; null for any other shape.</summary>
    private static string? NonNullType(JsonObject property)
    {
        if (property["type"] is not JsonArray { Count: 2 } types)
        {
            return null;
        }

        var names = types.Select(t => t?.GetValue<string>()).ToArray();
        return names.Count(n => n == "null") == 1 ? names.Single(n => n != "null") : null;
    }

    /// <summary>Every property of every object in the schema, with whether its object requires it.</summary>
    private static IEnumerable<(JsonNode Property, bool Required)> Properties(JsonNode node)
    {
        if (node is JsonObject obj && obj["properties"] is JsonObject properties)
        {
            var required = (obj["required"] as JsonArray)?.Select(r => r?.GetValue<string>()).ToHashSet() ?? [];
            foreach (var (name, property) in properties)
            {
                if (property is not null)
                {
                    yield return (property, required.Contains(name));
                }
            }
        }

        foreach (var child in Children(node))
        {
            foreach (var found in Properties(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>The subschemas directly below a node: its properties, items, union branches and definitions.</summary>
    private static IEnumerable<JsonNode> Children(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        yield return item;
                    }
                }

                break;

            case JsonObject obj:
                foreach (var key in new[] { "items", "anyOf", "oneOf", "allOf" })
                {
                    if (obj[key] is { } child)
                    {
                        yield return child;
                    }
                }

                foreach (var key in new[] { "properties", "$defs", "definitions" })
                {
                    if (obj[key] is JsonObject map)
                    {
                        foreach (var (_, child) in map)
                        {
                            if (child is not null)
                            {
                                yield return child;
                            }
                        }
                    }
                }

                break;
        }
    }
}
