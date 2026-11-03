using Nornis.Application.Configuration;
using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// The answer to "who pays for this call, and on which model" (feature 39), decided by
/// <see cref="IAiFundingService"/> alone.
/// </summary>
/// <param name="Model">The provider's model or deployment name, as sent on the wire.</param>
/// <param name="AiKeyId">The key that pays, when <paramref name="FundedBy"/> is a key.</param>
/// <param name="KeyHolderUserId">The user whose keys or trial pay; null when there is none.</param>
/// <param name="ModelId">The catalog id, when a catalog model runs; null otherwise.</param>
/// <param name="Price">The price of a key-funded model, from the catalog or the user's own
/// OpenAI-compatible line. Null when Nornis's key runs, which is priced from <c>ModelPricing</c>.</param>
public sealed record AiFunding(
    AiProvider Provider,
    string Model,
    AiFundedBy FundedBy,
    Guid? AiKeyId = null,
    Guid? KeyHolderUserId = null,
    string? ModelId = null,
    AiModelPrice? Price = null);
