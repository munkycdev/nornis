using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>What paid for a group of AI calls (feature 39): the funding kind and, for a user's own
/// key, which key. Both null on rows written before feature 39, which Nornis paid for.</summary>
public sealed record AiFundingKey(AiFundedBy? FundedBy, Guid? AiKeyId);
