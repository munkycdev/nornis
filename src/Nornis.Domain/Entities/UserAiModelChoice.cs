using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// The model a user picked for one task (feature 39). No row means the task's default, never
/// "off".
/// </summary>
public class UserAiModelChoice
{
    public Guid UserId { get; set; }

    public AiTask Task { get; set; }

    /// <summary>A catalog id (<c>openai/gpt-5.4</c>), <c>compatible:{UserAiCompatibleModel.Id}</c>, or
    /// <c>untested:{AiProvider}/{model}</c> for a model the key's provider lists that the catalog
    /// doesn't (Dave's picker decision, 2026-11-03).</summary>
    public string ModelId { get; set; } = string.Empty;

    public const string CompatiblePrefix = "compatible:";

    /// <summary>Also marks the ledger rows such a model ran, whose price is an estimate.</summary>
    public const string UntestedPrefix = "untested:";
}
