namespace Nornis.Infrastructure.Ai;

/// <summary>
/// A JSON schema for structured output, written in the one dialect every provider accepts in its
/// strictest mode (feature 39): every object closed (<c>"additionalProperties": false</c>), every
/// property listed in <c>required</c>, optional values as a <c>null</c> union.
/// <c>StrictSchemaDialectTests</c> holds every schema a client sends to that rule.
/// Anthropic also caps how many union and optional properties one schema may have, which the
/// dialect alone does not keep under; its traits fit a schema to those caps on the way out
/// (<see cref="AnthropicSchemaLimits"/>), so the schema written here stays the one dialect.
/// </summary>
public sealed record AiJsonSchema(string Name, string Schema);

/// <summary>
/// What a client asks of a call beyond its messages, in provider-neutral terms. Each provider's
/// <see cref="IAiProviderTraits"/> turns it into the options that provider understands.
/// </summary>
public sealed record AiCallOptions(AiJsonSchema? Schema = null, int? MaxOutputTokens = null);
