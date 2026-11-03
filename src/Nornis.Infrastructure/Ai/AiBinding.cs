using Microsoft.Extensions.AI;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>One AI call's client, its provider's traits, and who pays for it (feature 39).</summary>
/// <param name="KeyVersion">The paying key's <c>UpdatedAt</c> when a key pays, so a refusal marks
/// the key that was used and not one saved since.</param>
public sealed record AiBinding(IChatClient Client, IAiProviderTraits Traits, AiFunding Funding, DateTimeOffset? KeyVersion = null);
