using Microsoft.Extensions.AI;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>One AI call's client, its provider's traits, and who pays for it (feature 39).</summary>
public sealed record AiBinding(IChatClient Client, IAiProviderTraits Traits, AiFunding Funding);
